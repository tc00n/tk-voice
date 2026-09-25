using System.Text.Json;

namespace TKVoice.Core.Dictionary;

/// <summary>
/// The local personal dictionary (FR-018): names, products, abbreviations, technical terms.
/// Persisted as JSON next to the settings (NFR-004). Thread-safe.
/// </summary>
public sealed class PersonalDictionary
{
    public const int MaxTermLength = 60;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Lock _gate = new();
    private List<string> _terms;

    public PersonalDictionary(string filePath)
    {
        _filePath = filePath;
        _terms = Load(filePath);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<string> Terms
    {
        get
        {
            lock (_gate)
            {
                return _terms.ToArray();
            }
        }
    }

    /// <summary>Checks a candidate term: one line, reasonable length, contains a letter or digit.</summary>
    public static bool TryNormalize(string? candidate, out string term, out string error)
    {
        term = (candidate ?? string.Empty).Trim().Trim('"', '„', '“', '\'').Trim();
        if (term.Length == 0 || !term.Any(char.IsLetterOrDigit))
        {
            error = "Kein Begriff markiert.";
            return false;
        }

        if (term.Contains('\n') || term.Contains('\r') || term.Contains('\t') || term.Length > MaxTermLength)
        {
            error = $"Bitte nur einen einzelnen Begriff markieren (max. {MaxTermLength} Zeichen).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Adds a term. Returns false if it is invalid or already present (case-insensitive).</summary>
    public bool Add(string candidate)
    {
        if (!TryNormalize(candidate, out var term, out _))
        {
            return false;
        }

        lock (_gate)
        {
            if (_terms.Any(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _terms = [.. _terms, term];
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Replaces <paramref name="existing"/> with <paramref name="replacement"/>, keeping its position.</summary>
    public bool Update(string existing, string replacement)
    {
        if (!TryNormalize(replacement, out var term, out _))
        {
            return false;
        }

        lock (_gate)
        {
            var index = _terms.FindIndex(t => string.Equals(t, existing, StringComparison.Ordinal));
            var duplicate = _terms.Any(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(t, existing, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || duplicate)
            {
                return false;
            }

            var updated = _terms.ToList();
            updated[index] = term;
            _terms = updated;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(string term)
    {
        lock (_gate)
        {
            var updated = _terms.Where(t => !string.Equals(t, term, StringComparison.Ordinal)).ToList();
            if (updated.Count == _terms.Count)
            {
                return false;
            }

            _terms = updated;
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(new DictionaryFile { Terms = _terms }, Options));
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static List<string> Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        using var stream = File.OpenRead(filePath);
        var file = JsonSerializer.Deserialize<DictionaryFile>(stream, Options);
        return file?.Terms.Where(t => TryNormalize(t, out _, out _)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    private sealed class DictionaryFile
    {
        public List<string> Terms { get; set; } = [];
    }
}
