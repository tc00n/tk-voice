using System.Text.RegularExpressions;

namespace TKVoice.Core.Dictionary;

/// <summary>
/// Finds explicitly spelled terms in a transcript ("NEONEX, geschrieben N-E-O-N-E-X") so they can be
/// learned into the personal dictionary (FR-021). Only explicit trigger words count; implicit spelling
/// is left to smart processing and never learned automatically.
/// </summary>
public static partial class SpelledTermDetector
{
    private const int MinLetters = 2;

    /// <param name="transcript">The raw transcript, where the spelled letters are still visible.</param>
    /// <param name="finalText">The text actually inserted; used to pick the intended capitalization.</param>
    public static IReadOnlyList<string> Detect(string transcript, string finalText)
    {
        var terms = new List<string>();
        foreach (Match match in SpellingPattern().Matches(transcript))
        {
            var letters = string.Concat(LetterPattern().Matches(match.Groups["letters"].Value).Select(m => m.Value));
            if (letters.Length < MinLetters || letters.Length > PersonalDictionary.MaxTermLength)
            {
                continue;
            }

            // Prefer the spelling as it appears in the inserted text (e.g. "Kuhn" rather than "KUHN").
            var inFinal = WordPattern().Matches(finalText)
                .Select(m => m.Value)
                .FirstOrDefault(word => string.Equals(word, letters, StringComparison.OrdinalIgnoreCase));
            var term = inFinal ?? letters;

            if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                terms.Add(term);
            }
        }

        return terms;
    }

    // A trigger word followed by single letters or digits separated by hyphens, dots, commas or spaces.
    [GeneratedRegex(
        @"\b(geschrieben|buchstabiert|spelled|spelt)\b[\s:,]*(?<letters>[\p{L}\p{N}](?:[\s\-.,]+[\p{L}\p{N}](?![\p{L}\p{N}]))+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SpellingPattern();

    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex LetterPattern();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
