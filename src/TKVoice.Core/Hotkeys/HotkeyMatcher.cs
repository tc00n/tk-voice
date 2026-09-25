namespace TKVoice.Core.Hotkeys;

/// <summary>
/// Turns raw key down/up events into gesture press/release transitions.
/// Not thread-safe; feed it from a single thread.
/// </summary>
public sealed class HotkeyMatcher
{
    private readonly Dictionary<HotkeyAction, HotkeyGesture> _gestures = new();
    private readonly HashSet<HotkeyAction> _active = new();
    private readonly HashSet<int> _pressed = new();

    public void Register(HotkeyAction action, HotkeyGesture gesture)
    {
        _gestures[action] = gesture;
        _active.Remove(action);
    }

    public IReadOnlySet<int> PressedKeys => _pressed;

    public IReadOnlyCollection<HotkeyAction> ActiveActions => _active;

    public bool IsRelevant(int virtualKey) => _gestures.Values.Any(g => g.AllKeys.Contains(virtualKey));

    /// <summary>Processes a key event and returns the gesture transitions it caused.</summary>
    public IReadOnlyList<(HotkeyAction Action, bool Pressed)> OnKey(int virtualKey, bool isDown)
    {
        var changed = isDown ? _pressed.Add(virtualKey) : _pressed.Remove(virtualKey);
        if (!changed)
        {
            // Auto-repeat or unmatched release.
            return [];
        }

        var transitions = new List<(HotkeyAction, bool)>();
        foreach (var (action, gesture) in _gestures)
        {
            var held = gesture.IsHeld(_pressed);
            if (held && _active.Add(action))
            {
                transitions.Add((action, true));
            }
            else if (!held && _active.Remove(action))
            {
                transitions.Add((action, false));
            }
        }

        return transitions;
    }
}
