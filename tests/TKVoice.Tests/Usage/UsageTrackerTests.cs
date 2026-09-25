using TKVoice.Core.Settings;
using TKVoice.Core.Usage;

namespace TKVoice.Tests.Usage;

public class UsageTrackerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tkvoice-tests-" + Guid.NewGuid().ToString("N"));
    private readonly CostSettings _costs = new();
    private DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(2));

    private string FilePath => Path.Combine(_directory, "usage.json");

    private UsageTracker Create() => new(FilePath, () => _costs, () => _now);

    [Fact]
    public void Estimates_cost_from_audio_minutes_and_tokens()
    {
        var tracker = Create();

        tracker.RecordTranscription(TimeSpan.FromMinutes(10));                          // 10 × 0.017 = 0.17
        tracker.RecordSmartProcessing(1_000_000, 100_000, fastMode: false);             // 0.10 + 0.05 = 0.15
        tracker.RecordSmartProcessing(1_000_000, 0, fastMode: true);                    // 2 × 0.10 = 0.20

        var month = tracker.CurrentMonth;
        Assert.Equal("2026-09", month.Month);
        Assert.Equal(0.52, month.EstimatedCostUsd, 6);
        Assert.Equal(1, month.TranscriptionRequests);
        Assert.Equal(600, month.TranscriptionSeconds, 6);
        Assert.Equal(2, month.SmartRequests);
        Assert.Equal(2_000_000, month.SmartInputTokens);
    }

    [Fact]
    public void Persists_and_starts_a_new_month()
    {
        Create().RecordTranscription(TimeSpan.FromMinutes(1));

        var reloaded = Create();
        Assert.Equal(1, reloaded.CurrentMonth.TranscriptionRequests);

        _now = _now.AddMonths(1);
        Assert.Equal(0, reloaded.CurrentMonth.TranscriptionRequests);
        Assert.Equal("2026-10", reloaded.CurrentMonth.Month);
    }

    [Fact]
    public void Warns_once_per_threshold_and_blocks_at_the_limit()
    {
        _costs.MonthlyLimitUsd = 1.0;
        var tracker = Create();
        var warnings = new List<string>();
        tracker.BudgetWarning += (_, message) => warnings.Add(message);

        tracker.RecordTranscription(TimeSpan.FromMinutes(30)); // 0.51 → 51 %
        tracker.RecordTranscription(TimeSpan.FromMinutes(1));  // still > 50 %, no repeat
        Assert.Single(warnings);
        Assert.StartsWith("50 %", warnings[0]);
        Assert.False(tracker.IsBudgetExhausted);

        tracker.RecordTranscription(TimeSpan.FromMinutes(30)); // 1.037 → 80 % and 100 %
        Assert.Equal(3, warnings.Count);
        Assert.Contains("gesperrt", warnings[2]);
        Assert.True(tracker.IsBudgetExhausted);

        _costs.MonthlyLimitUsd = 5;
        Assert.False(tracker.IsBudgetExhausted); // raising the limit unblocks
    }

    [Fact]
    public void No_limit_never_blocks_or_warns()
    {
        var tracker = Create();
        var warnings = 0;
        tracker.BudgetWarning += (_, _) => warnings++;

        tracker.RecordTranscription(TimeSpan.FromHours(100));

        Assert.False(tracker.IsBudgetExhausted);
        Assert.Equal(0, warnings);
    }

    [Fact]
    public void Usage_file_contains_no_content_fields()
    {
        Create().RecordSmartProcessing(10, 5, fastMode: false);
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(FilePath));

        var properties = json.RootElement.EnumerateArray().SelectMany(m => m.EnumerateObject()).Select(p => p.Name).ToHashSet();
        Assert.Subset(
            new HashSet<string>
            {
                "Month", "TranscriptionRequests", "TranscriptionSeconds", "SmartRequests",
                "SmartInputTokens", "SmartOutputTokens", "EstimatedCostUsd", "NotifiedThresholds",
            },
            properties);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
