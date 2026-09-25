using TKVoice.Core.Settings;

namespace TKVoice.Core.Dictation;

public enum InsertionMethod
{
    /// <summary>Clipboard + Ctrl+V with clipboard preservation. One undo step, newlines never trigger "send".</summary>
    Paste,

    /// <summary>Synthesized Unicode keystrokes. Leaves the clipboard untouched.</summary>
    Type,
}

public static class InsertionMethodSelector
{
    public static InsertionMethod Select(InsertionSettings settings, string processName)
    {
        if (settings.TypeInsteadOfPasteProcesses.Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase)))
        {
            return InsertionMethod.Type;
        }

        return Enum.TryParse<InsertionMethod>(settings.Method, ignoreCase: true, out var method) ? method : InsertionMethod.Paste;
    }
}
