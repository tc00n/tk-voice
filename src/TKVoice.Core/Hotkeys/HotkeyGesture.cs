using System.Collections.Frozen;

namespace TKVoice.Core.Hotkeys;

/// <summary>
/// A combination of keys that must be held together, e.g. "RightCtrl" or "Ctrl+Win".
/// Each part accepts one or more Windows virtual-key codes, so "Ctrl" matches both left and right Ctrl.
/// </summary>
public sealed class HotkeyGesture
{
    private static readonly FrozenDictionary<string, int[]> NamedKeys = BuildNamedKeys();

    private HotkeyGesture(string text, IReadOnlyList<int[]> parts)
    {
        Text = text;
        Parts = parts;
    }

    public string Text { get; }

    /// <summary>Each part is a set of alternative virtual-key codes; all parts must be held.</summary>
    public IReadOnlyList<int[]> Parts { get; }

    public IEnumerable<int> AllKeys => Parts.SelectMany(p => p);

    public bool IsHeld(IReadOnlySet<int> pressedKeys) => Parts.All(part => part.Any(pressedKeys.Contains));

    public override string ToString() => Text;

    public static HotkeyGesture Parse(string text)
    {
        if (!TryParse(text, out var gesture, out var error))
        {
            throw new FormatException(error);
        }

        return gesture;
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture, out string error)
    {
        gesture = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey ist leer.";
            return false;
        }

        var parts = new List<int[]>();
        foreach (var token in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!NamedKeys.TryGetValue(token, out var keys))
            {
                error = $"Unbekannte Taste '{token}' in Hotkey '{text}'.";
                return false;
            }

            parts.Add(keys);
        }

        if (parts.Count == 0)
        {
            error = $"Hotkey '{text}' enthält keine Taste.";
            return false;
        }

        gesture = new HotkeyGesture(string.Join('+', text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)), parts);
        error = string.Empty;
        return true;
    }

    private static FrozenDictionary<string, int[]> BuildNamedKeys()
    {
        const int LShift = 0xA0, RShift = 0xA1, LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, RAlt = 0xA5, LWin = 0x5B, RWin = 0x5C;

        var keys = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = [LCtrl, RCtrl],
            ["LeftCtrl"] = [LCtrl],
            ["RightCtrl"] = [RCtrl],
            ["Shift"] = [LShift, RShift],
            ["LeftShift"] = [LShift],
            ["RightShift"] = [RShift],
            ["Alt"] = [LAlt, RAlt],
            ["LeftAlt"] = [LAlt],
            ["RightAlt"] = [RAlt],
            ["Win"] = [LWin, RWin],
            ["LeftWin"] = [LWin],
            ["RightWin"] = [RWin],
            ["Space"] = [0x20],
            ["CapsLock"] = [0x14],
            ["ScrollLock"] = [0x91],
            ["Pause"] = [0x13],
            ["Insert"] = [0x2D],
            ["Home"] = [0x24],
            ["End"] = [0x23],
            ["PageUp"] = [0x21],
            ["PageDown"] = [0x22],
            ["Menu"] = [0x5D],
        };

        for (var c = 'A'; c <= 'Z'; c++)
        {
            keys[c.ToString()] = [c];
        }

        for (var d = 0; d <= 9; d++)
        {
            keys[d.ToString()] = ['0' + d];
        }

        for (var f = 1; f <= 24; f++)
        {
            keys[$"F{f}"] = [0x6F + f];
        }

        return keys.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
