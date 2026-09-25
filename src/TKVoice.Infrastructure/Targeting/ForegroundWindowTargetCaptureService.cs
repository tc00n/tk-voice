using System.Diagnostics;
using TKVoice.Core.Abstractions;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.Targeting;

/// <summary>Captures the foreground window as insertion target. Reads no window content (FR-023).</summary>
public sealed class ForegroundWindowTargetCaptureService(ILog log) : ITargetCaptureService
{
    public DictationTarget? CaptureCurrentTarget()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return null;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == Environment.ProcessId)
        {
            log.Debug("Foreground window belongs to TK Voice itself; no target.");
            return null;
        }

        return new DictationTarget(hwnd, (int)processId, GetProcessName((int)processId), GetFocusedControl(threadId));
    }

    /// <summary>
    /// The focused native child control (classic Win32 apps). Chromium/Electron/UWP apps have a
    /// single native window and remember their internal focus themselves.
    /// </summary>
    private static nint GetFocusedControl(uint threadId)
    {
        var info = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        return NativeMethods.GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : 0;
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
