namespace TKVoice.Core.Abstractions;

/// <summary>
/// The insertion target captured when a dictation starts (FR-025). Holds no content from the target.
/// </summary>
/// <param name="WindowHandle">Top-level foreground window.</param>
/// <param name="FocusHandle">Focused child control, if the application exposes one as a native window; otherwise 0.</param>
public sealed record DictationTarget(nint WindowHandle, int ProcessId, string ProcessName, nint FocusHandle = 0)
{
    /// <summary>Only captured when enabled in the settings. Never logged: titles can contain e-mail subjects etc.</summary>
    public string? WindowTitle { get; init; }

    public override string ToString() => $"{ProcessName} (pid {ProcessId}, hwnd 0x{WindowHandle:X}, focus 0x{FocusHandle:X})";
}

public interface ITargetCaptureService
{
    /// <summary>Captures the currently focused window, or null if there is no usable target.</summary>
    DictationTarget? CaptureCurrentTarget();
}
