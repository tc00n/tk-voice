using System.Windows.Controls;
using System.Windows.Input;
using TKVoice.Core.Hotkeys;

namespace TKVoice.App.Settings;

/// <summary>
/// Records a hotkey by pressing it: hold the keys, the combination is taken when all are released.
/// Right-hand modifiers are recorded as such ("RightCtrl"), left-hand ones generically ("Ctrl").
/// Esc cancels, Backspace/Delete clears (disables) the hotkey. Live hotkeys are suspended meanwhile.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private static readonly (Key Key, string Name)[] Modifiers =
    [
        (Key.LeftCtrl, "Ctrl"), (Key.RightCtrl, "RightCtrl"),
        (Key.LeftShift, "Shift"), (Key.RightShift, "RightShift"),
        (Key.LeftAlt, "Alt"), (Key.RightAlt, "RightAlt"),
        (Key.LWin, "Win"), (Key.RWin, "RightWin"),
    ];

    private readonly HashSet<string> _recorded = new(StringComparer.OrdinalIgnoreCase);
    private string _beforeRecording = string.Empty;
    private bool _recording;

    public HotkeyBox()
    {
        // Subclasses do not pick up the implicit (Fluent) TextBox style by themselves.
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        ToolTip = "Klicken und die gewünschte Tastenkombination drücken. Entf löscht, Esc bricht ab.";
    }

    /// <summary>Raised when a new combination (or empty = disabled) has been recorded.</summary>
    public event EventHandler? GestureChanged;

    public string Gesture
    {
        get => Text;
        set => Text = value;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _beforeRecording = Text;
        _recorded.Clear();
        _recording = true;
        Text = "Tasten drücken …";
        App.Current.SuspendHotkeys(true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (_recording)
        {
            Text = _beforeRecording;
            _recording = false;
        }

        App.Current.SuspendHotkeys(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        if (!_recording)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_recorded.Count == 0 && key == Key.Escape)
        {
            Finish(_beforeRecording);
            return;
        }

        if (_recorded.Count == 0 && key is Key.Back or Key.Delete)
        {
            Finish(string.Empty);
            return;
        }

        foreach (var (modifier, name) in Modifiers)
        {
            if (Keyboard.IsKeyDown(modifier))
            {
                _recorded.Add(name);
            }
        }

        if (KeyName(key) is { } keyName)
        {
            _recorded.Add(keyName);
        }

        Text = HotkeyGesture.Format(_recorded) ?? "Tasten drücken …";
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        e.Handled = true;
        if (!_recording || _recorded.Count == 0)
        {
            return;
        }

        var anyModifierDown = Modifiers.Any(m => Keyboard.IsKeyDown(m.Key));
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var otherKeyDown = KeyName(key) is null ? false : Keyboard.IsKeyDown(key);
        if (!anyModifierDown && !otherKeyDown)
        {
            Finish(HotkeyGesture.Format(_recorded) ?? _beforeRecording);
        }
    }

    private void Finish(string gesture)
    {
        _recording = false;
        Text = gesture;
        GestureChanged?.Invoke(this, EventArgs.Empty);
        Keyboard.ClearFocus();
    }

    /// <summary>Maps non-modifier keys to the names understood by <see cref="HotkeyGesture"/>.</summary>
    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space",
        Key.CapsLock => "CapsLock",
        Key.Scroll => "ScrollLock",
        Key.Pause => "Pause",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Apps => "Menu",
        _ => null,
    };
}
