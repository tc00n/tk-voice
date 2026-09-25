using System.Diagnostics;
using TKVoice.Core.Abstractions;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.Targeting;

/// <summary>Captures the foreground window as insertion target. Reads no window content (FR-023).</summary>
public sealed class ForegroundWindowTargetCaptureService(bool captureWindowTitle, ILog log) : ITargetCaptureService
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

        var (processName, description) = GetProcessInfo((int)processId);
        return new DictationTarget(hwnd, (int)processId, processName, GetFocusedControl(threadId))
        {
            WindowTitle = captureWindowTitle ? GetWindowTitle(hwnd) : null,
            ApplicationDescription = description,
        };
    }

    private static unsafe string GetWindowTitle(nint hwnd)
    {
        const int capacity = 512;
        var buffer = stackalloc char[capacity];
        var length = NativeMethods.GetWindowTextW(hwnd, buffer, capacity);
        return new string(buffer, 0, Math.Max(length, 0));
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

    private static (string Name, string? Description) GetProcessInfo(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            string? description = null;
            try
            {
                // Not accessible for elevated processes; the process name is enough then.
                description = process.MainModule?.FileVersionInfo.FileDescription?.Trim();
            }
            catch (Exception)
            {
            }

            return (process.ProcessName, string.IsNullOrEmpty(description) ? null : description);
        }
        catch (Exception)
        {
            return ("unknown", null);
        }
    }
}
