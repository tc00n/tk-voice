using System.Text.Json;
using TKVoice.Core.Settings;

namespace TKVoice.Core.Usage;

/// <summary>
/// Technical usage data per calendar month (FR-040): audio minutes, requests, tokens and an
/// estimated cost. Never stores audio or text. Enforces the local monthly budget (FR-041) and
/// reports each warning threshold once per month. Thread-safe.
/// </summary>
public sealed class UsageTracker
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Func<CostSettings> _costs;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, MonthlyUsage> _months;

    public UsageTracker(string filePath, Func<CostSettings> costs, Func<DateTimeOffset>? clock = null)
    {
        _filePath = filePath;
        _costs = costs;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _months = Load(filePath);
    }

    /// <summary>Raised when a warning threshold or the limit is reached; the message is user-facing.</summary>
    public event EventHandler<string>? BudgetWarning;

    public MonthlyUsage CurrentMonth
    {
        get
        {
            lock (_gate)
            {
                return Current().Copy();
            }
        }
    }

    public bool IsBudgetExhausted
    {
        get
        {
            var limit = _costs().MonthlyLimitUsd;
            return limit > 0 && CurrentMonth.EstimatedCostUsd >= limit;
        }
    }

    public void RecordTranscription(TimeSpan audio)
    {
        var costs = _costs();
        Update(month =>
        {
            month.TranscriptionRequests++;
            month.TranscriptionSeconds += audio.TotalSeconds;
            month.EstimatedCostUsd += audio.TotalMinutes * costs.TranscriptionUsdPerMinute;
        });
    }

    public void RecordSmartProcessing(int? inputTokens, int? outputTokens, bool fastMode)
    {
        var costs = _costs();
        var multiplier = fastMode ? costs.FastModeMultiplier : 1.0;
        Update(month =>
        {
            month.SmartRequests++;
            month.SmartInputTokens += inputTokens ?? 0;
            month.SmartOutputTokens += outputTokens ?? 0;
            month.EstimatedCostUsd += multiplier *
                ((inputTokens ?? 0) * costs.SmartInputUsdPerMillionTokens + (outputTokens ?? 0) * costs.SmartOutputUsdPerMillionTokens) / 1_000_000;
        });
    }

    private void Update(Action<MonthlyUsage> change)
    {
        var warnings = new List<string>();
        lock (_gate)
        {
            var month = Current();
            change(month);
            warnings.AddRange(CheckThresholds(month));
            Save();
        }

        foreach (var warning in warnings)
        {
            BudgetWarning?.Invoke(this, warning);
        }
    }

    /// <summary>Caller holds the lock.</summary>
    private IEnumerable<string> CheckThresholds(MonthlyUsage month)
    {
        var costs = _costs();
        if (costs.MonthlyLimitUsd <= 0)
        {
            yield break;
        }

        var percent = month.EstimatedCostUsd / costs.MonthlyLimitUsd * 100;
        foreach (var threshold in costs.WarningThresholdsPercent.Where(t => t is > 0 and < 100).Append(100).Distinct().Order())
        {
            if (percent >= threshold && !month.NotifiedThresholds.Contains(threshold))
            {
                month.NotifiedThresholds.Add(threshold);
                yield return threshold >= 100
                    ? $"Monatsbudget von {costs.MonthlyLimitUsd:0.00} $ erreicht (Schätzung). Diktieren ist bis Monatsende gesperrt – Limit in den Einstellungen anpassbar."
                    : $"{threshold} % des Monatsbudgets erreicht: {month.EstimatedCostUsd:0.00} $ von {costs.MonthlyLimitUsd:0.00} $ (Schätzung).";
            }
        }
    }

    /// <summary>Caller holds the lock.</summary>
    private MonthlyUsage Current()
    {
        var key = _clock().ToString("yyyy-MM");
        if (!_months.TryGetValue(key, out var month))
        {
            _months[key] = month = new MonthlyUsage { Month = key };
        }

        return month;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_months.Values.OrderBy(m => m.Month), Options));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static Dictionary<string, MonthlyUsage> Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                using var stream = File.OpenRead(filePath);
                var months = JsonSerializer.Deserialize<List<MonthlyUsage>>(stream, Options) ?? [];
                return months.ToDictionary(m => m.Month);
            }
        }
        catch (JsonException)
        {
            // A damaged usage file must not stop dictation; start counting anew.
        }

        return [];
    }
}

public sealed class MonthlyUsage
{
    public string Month { get; set; } = string.Empty;
    public int TranscriptionRequests { get; set; }
    public double TranscriptionSeconds { get; set; }
    public int SmartRequests { get; set; }
    public long SmartInputTokens { get; set; }
    public long SmartOutputTokens { get; set; }
    public double EstimatedCostUsd { get; set; }
    public List<int> NotifiedThresholds { get; set; } = [];

    public MonthlyUsage Copy() => new()
    {
        Month = Month,
        TranscriptionRequests = TranscriptionRequests,
        TranscriptionSeconds = TranscriptionSeconds,
        SmartRequests = SmartRequests,
        SmartInputTokens = SmartInputTokens,
        SmartOutputTokens = SmartOutputTokens,
        EstimatedCostUsd = EstimatedCostUsd,
        NotifiedThresholds = [.. NotifiedThresholds],
    };
}
