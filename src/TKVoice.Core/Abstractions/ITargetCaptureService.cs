namespace TKVoice.Core.Abstractions;

/// <summary>
/// The insertion target captured when a dictation starts (FR-025). Holds no content from the target.
/// </summary>
public sealed record DictationTarget(nint WindowHandle, int ProcessId, string ProcessName)
{
    public override string ToString() => $"{ProcessName} (pid {ProcessId}, hwnd 0x{WindowHandle:X})";
}

public interface ITargetCaptureService
{
    /// <summary>Captures the currently focused window, or null if there is no usable target.</summary>
    DictationTarget? CaptureCurrentTarget();
}
