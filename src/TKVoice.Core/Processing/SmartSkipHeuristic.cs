using System.Text.RegularExpressions;

namespace TKVoice.Core.Processing;

/// <summary>
/// Decides whether a transcript is already clean enough to insert without the smart processing
/// round trip, saving its latency. Deliberately conservative: any hint of fillers, self-corrections,
/// spoken commands, spelling, number words or repetitions sends the text through smart processing.
/// </summary>
public static partial class SmartSkipHeuristic
{
    public const int MaxWords = 20;

    private static readonly HashSet<string> TriggerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fillers (de/en)
        "äh", "ähm", "öh", "öhm", "hm", "hmm", "mhm", "quasi", "sozusagen", "halt", "irgendwie", "also",
        "uh", "um", "uhm", "erm", "like",

        // Self-corrections
        "nein", "ne", "nee", "korrektur", "korrigiere", "sorry", "moment", "falsch", "stopp", "stop", "no", "wait", "actually",

        // Formatting and editing commands
        "zeile", "absatz", "punkt", "komma", "doppelpunkt", "semikolon", "klammer", "fragezeichen", "ausrufezeichen",
        "bindestrich", "anführungszeichen", "liste", "aufzählung", "aufzählungspunkt", "aufzählungspunkte",
        "bulletpoint", "bulletpoints", "stichpunkt", "stichpunkte", "überschrift", "lösche", "löschen", "ersetze",
        "zurück", "newline", "paragraph", "bullet", "bullets", "heading", "delete", "replace", "colon", "semicolon",

        // Spelling
        "geschrieben", "buchstabiert", "buchstabieren", "spelled", "spelt",

        // Units and number context
        "prozent", "euro", "cent", "dollar", "uhr", "percent",
    };

    public static bool CanSkip(string transcript)
    {
        var words = WordPattern().Matches(transcript).Select(m => m.Value).ToList();
        if (words.Count == 0 || words.Count > MaxWords)
        {
            return false;
        }

        if (words.Any(TriggerWords.Contains) || words.Any(IsNumberWord))
        {
            return false;
        }

        // "ich meine", "I mean", "oder besser", "or rather"
        if (CorrectionPhrasePattern().IsMatch(transcript))
        {
            return false;
        }

        if (!LooksPunctuated(transcript))
        {
            return false;
        }

        // Immediate repetitions ("das das") and spelled-out letters ("N-E-O", "N E O N").
        for (var i = 1; i < words.Count; i++)
        {
            if (string.Equals(words[i], words[i - 1], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return !SpelledLettersPattern().IsMatch(transcript);
    }

    /// <summary>
    /// Skipping relies on the transcription's punctuation. Require a capitalized start, closing
    /// punctuation and a comma before German subordinating conjunctions (mandatory by German rules).
    /// </summary>
    private static bool LooksPunctuated(string transcript)
    {
        var text = transcript.Trim();
        return text.Length > 0
            && !char.IsLower(text[0])
            && ".?!".Contains(text[^1])
            && !ConjunctionWithoutCommaPattern().IsMatch(text);
    }

    private static bool IsNumberWord(string word) =>
        NumberWordPattern().IsMatch(word);

    [GeneratedRegex(@"[\p{L}\p{N}]\s+(dass|weil|obwohl|damit|sodass|nachdem|bevor|während|falls|sobald|wenn|ob|sondern)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConjunctionWithoutCommaPattern();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\b(ich meine|ich mein|oder besser|oder vielmehr|i mean|or rather|scratch that)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CorrectionPhrasePattern();

    [GeneratedRegex(@"\b\p{L}(?:[\s\-.]+\p{L}\b){2,}")]
    private static partial Regex SpelledLettersPattern();

    // German number words are compounds ("dreitausendzweihundert", "zwanzigste"), so the large stems
    // match anywhere in a word; small numbers and ordinals must match the whole word.
    [GeneratedRegex(
        @"(zehn|zwanzig|dreißig|vierzig|fünfzig|sechzig|siebzig|achtzig|neunzig|hundert|tausend|million|milliard)|" +
        @"^(eins|zwei|drei|vier|fünf|sechs|sieben|acht|neun|elf|zwölf|" +
        @"erste[nrms]?|zweite[nrms]?|dritte[nrms]?|vierte[nrms]?|fünfte[nrms]?|" +
        @"one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|" +
        @"seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|thousand|billion|" +
        @"first|second|third|fourth|fifth)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NumberWordPattern();
}
