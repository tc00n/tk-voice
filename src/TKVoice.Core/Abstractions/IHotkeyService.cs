using TKVoice.Core.Hotkeys;

namespace TKVoice.Core.Abstractions;

/// <summary>
/// Global hotkeys with press and release notification. Events are raised sequentially on a
/// dedicated thread, never on the keyboard hook callback itself.
/// </summary>
public interface IHotkeyService : IDisposable
{
    event EventHandler<HotkeyAction>? Pressed;
    event EventHandler<HotkeyAction>? Released;

    /// <summary>
    /// While true, no hotkey events are raised and no keys are swallowed, e.g. while the settings
    /// window records a new hotkey.
    /// </summary>
    bool Suspended { get; set; }

    void Register(HotkeyAction action, HotkeyGesture gesture);
    void Start();
}
