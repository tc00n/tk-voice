namespace TKVoice.Core.Processing;

/// <summary>
/// App-specific smart processing style (FR-024). Matched by process name only; TK Voice never reads
/// application content (FR-023). Applications without a rule use the global Smart Mode.
/// </summary>
public sealed class AppRule
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Process names without ".exe", case-insensitive.</summary>
    public List<string> Processes { get; set; } = [];

    /// <summary>Style guidance appended to the smart processing input.</summary>
    public string Style { get; set; } = string.Empty;

    public static AppRule? Find(IEnumerable<AppRule> rules, string processName) =>
        rules.FirstOrDefault(rule => rule.Processes.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase)));

    public static List<AppRule> Defaults() =>
    [
        new()
        {
            Name = "Chat",
            Processes = ["ms-teams", "Teams", "slack", "WhatsApp", "WhatsApp.Root", "Signal", "Telegram", "Discord", "Element"],
            Style = "Chat message. Natural, conversational and fairly concise. Light edits only: remove fillers and fix " +
                    "obvious errors, keep the casual tone and the user's wording. No greeting or sign-off unless dictated. " +
                    "No headings.",
        },
        new()
        {
            Name = "E-Mail",
            Processes = ["OUTLOOK", "olk", "thunderbird", "HxOutlook"],
            Style = "E-mail. Complete, well-formed sentences; smooth spoken phrasing more strongly. Professional but not " +
                    "stiff. If a greeting or sign-off is dictated, put it on its own line, separated from the body by a " +
                    "blank line. Never invent a greeting, sign-off or signature.",
        },
        new()
        {
            Name = "Dokument",
            Processes = ["WINWORD", "ONENOTE", "Notion", "Obsidian"],
            Style = "Document text. Clean, well-structured paragraphs; use a list where the content is an enumeration. " +
                    "Headings only when dictated.",
        },
        new()
        {
            Name = "Präsentation",
            Processes = ["POWERPNT"],
            Style = "Slide text. Concise, bullet-friendly phrasing without filler sentences; keep all dictated points.",
        },
        new()
        {
            Name = "Entwicklung",
            Processes =
            [
                "Code", "Code - Insiders", "Cursor", "Windsurf", "devenv", "rider64", "idea64", "pycharm64",
                "WindowsTerminal", "powershell", "pwsh", "cmd", "claude",
            ],
            Style = "Developer tool, terminal or prompt to an AI assistant. Minimal rewording: keep the user's phrasing, " +
                    "only remove fillers and fix obvious recognition errors. Preserve technical terms, identifiers, file " +
                    "names, commands and code exactly, including casing (e.g. useState, git rebase, README.md, snake_case). " +
                    "Spoken \"Punkt\"/\"dot\" inside a file name or identifier becomes \".\". Never translate technical " +
                    "terms. No Markdown unless dictated.",
        },
    ];
}
