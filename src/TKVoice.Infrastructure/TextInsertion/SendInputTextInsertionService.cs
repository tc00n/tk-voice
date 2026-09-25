using System.Diagnostics;
using System.Runtime.InteropServices;
using TKVoice.Core.Abstractions;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.TextInsertion;

/// <summary>
/// Inserts text by synthesizing Unicode keystrokes (SendInput with KEYEVENTF_UNICODE) into the
/// original target window. Leaves the clipboard untouched. Refuses to type anywhere else (FR-027).
/// </summary>
public sealed class SendInputTextInsertionService(ILog log) : ITextInsertionService
{
    private const int CharactersPerBatch = 200;
    private static readonly int[] ModifierKeys = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FocusSettleTimeout = TimeSpan.FromMilliseconds(500);

    public async Task<InsertionResult> InsertAsync(DictationTarget target, string text, CancellationToken cancellationToken)
    {
        if (!NativeMethods.IsWindow(target.WindowHandle))
        {
            return InsertionResult.TargetUnavailable;
        }

        var stopwatch = Stopwatch.StartNew();

        // Held modifiers would turn the typed characters into shortcuts (e.g. Ctrl+Enter).
        await WaitForModifiersReleasedAsync(cancellationToken);
        var modifiersMs = stopwatch.ElapsedMilliseconds;

        if (!await EnsureForegroundAsync(target.WindowHandle, cancellationToken))
        {
            return InsertionResult.TargetUnavailable;
        }

        var focusMs = stopwatch.ElapsedMilliseconds - modifiersMs;

        TypeText(text);
        var typingMs = stopwatch.ElapsedMilliseconds - modifiersMs - focusMs;
        log.Info($"Insertion timing: modifiers {modifiersMs} ms, focus {focusMs} ms, typing {typingMs} ms.");
        return InsertionResult.Inserted;
    }

    private async Task WaitForModifiersReleasedAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ModifierReleaseTimeout;
        while (ModifierKeys.Any(NativeMethods.IsKeyPhysicallyDown))
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                log.Warn("Modifier keys still held; inserting anyway.");
                return;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private async Task<bool> EnsureForegroundAsync(nint hwnd, CancellationToken cancellationToken)
    {
        if (NativeMethods.GetForegroundWindow() == hwnd)
        {
            return true;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        if (!NativeMethods.SetForegroundWindow(hwnd))
        {
            // Windows only lets the foreground thread hand over focus; borrow its input queue.
            var foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
            var ownThread = NativeMethods.GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != ownThread
                && NativeMethods.AttachThreadInput(ownThread, foregroundThread, true);
            try
            {
                NativeMethods.BringWindowToTop(hwnd);
                NativeMethods.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(ownThread, foregroundThread, false);
                }
            }
        }

        var deadline = DateTimeOffset.UtcNow + FocusSettleTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                log.Debug("Target window refocused.");
                return true;
            }

            await Task.Delay(10, cancellationToken);
        }

        log.Warn("Target window could not be refocused.");
        return false;
    }

    private static unsafe void TypeText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        for (var offset = 0; offset < normalized.Length; offset += CharactersPerBatch)
        {
            var chunk = normalized.AsSpan(offset, Math.Min(CharactersPerBatch, normalized.Length - offset));
            var inputs = new NativeMethods.INPUT[chunk.Length * 2];
            for (var i = 0; i < chunk.Length; i++)
            {
                var (down, up) = KeystrokeFor(chunk[i]);
                inputs[i * 2] = down;
                inputs[i * 2 + 1] = up;
            }

            fixed (NativeMethods.INPUT* pInputs = inputs)
            {
                var sent = NativeMethods.SendInput((uint)inputs.Length, pInputs, Marshal.SizeOf<NativeMethods.INPUT>());
                if (sent != inputs.Length)
                {
                    throw new InvalidOperationException(
                        $"Windows hat die Texteingabe blockiert (Win32-Fehler {Marshal.GetLastWin32Error()}). " +
                        "Läuft das Zielprogramm mit Administratorrechten?");
                }
            }
        }
    }

    private static (NativeMethods.INPUT Down, NativeMethods.INPUT Up) KeystrokeFor(char c) => c switch
    {
        '\n' => (VirtualKey(NativeMethods.VK_RETURN, keyUp: false), VirtualKey(NativeMethods.VK_RETURN, keyUp: true)),
        '\t' => (VirtualKey(NativeMethods.VK_TAB, keyUp: false), VirtualKey(NativeMethods.VK_TAB, keyUp: true)),
        _ => (Unicode(c, keyUp: false), Unicode(c, keyUp: true)),
    };

    private static NativeMethods.INPUT Unicode(char c, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wScan = c,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
            },
        },
    };

    private static NativeMethods.INPUT VirtualKey(ushort vk, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
            },
        },
    };
}
