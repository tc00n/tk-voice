using System.Diagnostics;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictation;
using TKVoice.Core.Settings;
using TKVoice.Infrastructure.Clipboard;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.TextInsertion;

/// <summary>
/// Inserts the final text into the target captured at dictation start (FR-025–FR-029):
/// refocus the original window and control, then paste via the clipboard (restoring the previous
/// clipboard content afterwards) or type Unicode keystrokes. Never inserts anywhere else.
/// </summary>
public sealed class WindowsTextInsertionService(
    Win32ClipboardService clipboard,
    InsertionSettings settings,
    IPasswordFieldDetector passwordFields,
    ILog log) : ITextInsertionService
{
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FocusSettleTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PasteReadTimeout = TimeSpan.FromSeconds(3);

    /// <summary>A clipboard restore from the previous paste that is still waiting for the target to read.</summary>
    private Task _pendingRestore = Task.CompletedTask;

    public async Task<InsertionResult> InsertAsync(DictationTarget target, string text, CancellationToken cancellationToken)
    {
        await _pendingRestore;

        if (!NativeMethods.IsWindow(target.WindowHandle))
        {
            return InsertionResult.TargetUnavailable;
        }

        var stopwatch = Stopwatch.StartNew();

        // Held modifiers would turn the insertion into shortcuts (e.g. Ctrl+Enter, Shift+Ctrl+V).
        if (!await KeyboardInput.WaitForModifiersReleasedAsync(ModifierReleaseTimeout, cancellationToken))
        {
            log.Warn("Modifier keys still held; inserting anyway.");
        }

        var modifiersMs = stopwatch.ElapsedMilliseconds;

        if (!await EnsureForegroundAsync(target.WindowHandle, cancellationToken))
        {
            return InsertionResult.TargetUnavailable;
        }

        RestoreFocusedControl(target);
        var focusMs = stopwatch.ElapsedMilliseconds - modifiersMs;

        // The field may have changed since the dictation started (FR-038).
        if (passwordFields.IsFocusInPasswordField())
        {
            return InsertionResult.PasswordField;
        }

        var method = InsertionMethodSelector.Select(settings, target.ProcessName);
        if (method == InsertionMethod.Paste && !TryPaste(text))
        {
            method = InsertionMethod.Type;
        }

        if (method == InsertionMethod.Type)
        {
            KeyboardInput.Type(text);
        }

        var insertMs = stopwatch.ElapsedMilliseconds - modifiersMs - focusMs;
        log.Info($"Insertion via {method}: modifiers {modifiersMs} ms, focus {focusMs} ms, insert {insertMs} ms.");
        return InsertionResult.Inserted;
    }

    private bool TryPaste(string text)
    {
        ClipboardSnapshot snapshot;
        Task textRead;
        try
        {
            snapshot = clipboard.Capture();
            textRead = clipboard.OfferTextForPaste(text);
        }
        catch (ClipboardUnavailableException ex)
        {
            log.Warn($"{ex.Message} Falling back to typing.");
            return false;
        }

        KeyboardInput.Paste();
        _pendingRestore = RestoreAfterReadAsync(snapshot, textRead);
        return true;
    }

    /// <summary>Restores the previous clipboard once the target has read the dictated text (FR-029).</summary>
    private async Task RestoreAfterReadAsync(ClipboardSnapshot snapshot, Task textRead)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var completed = await Task.WhenAny(textRead, Task.Delay(PasteReadTimeout));
            if (completed == textRead)
            {
                // Some applications read the clipboard more than once per paste.
                await Task.Delay(TimeSpan.FromMilliseconds(settings.ClipboardRestoreDelayMilliseconds));
            }
            else
            {
                log.Warn($"Target did not read the clipboard within {PasteReadTimeout.TotalSeconds:F0} s.");
            }

            var restored = clipboard.RestoreIfStillOwner(snapshot);
            log.Info(restored
                ? $"Clipboard restored ({snapshot.Formats.Count} formats) after {stopwatch.ElapsedMilliseconds} ms."
                : "Clipboard was replaced in the meantime; newer content kept.");
        }
        catch (Exception ex)
        {
            log.Error("Restoring the clipboard failed.", ex);
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
            // Windows refuses focus changes from background processes; become the last input source.
            KeyboardInput.TapUnassignedKey();
            if (!NativeMethods.SetForegroundWindow(hwnd))
            {
                WithAttachedInput(NativeMethods.GetForegroundWindow(), () =>
                {
                    NativeMethods.BringWindowToTop(hwnd);
                    NativeMethods.SetForegroundWindow(hwnd);
                });
            }
        }

        var deadline = DateTimeOffset.UtcNow + FocusSettleTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                log.Info("Original target window refocused.");
                return true;
            }

            await Task.Delay(10, cancellationToken);
        }

        log.Warn("Target window could not be refocused.");
        return false;
    }

    /// <summary>Puts keyboard focus back on the control that had it when the dictation started.</summary>
    private void RestoreFocusedControl(DictationTarget target)
    {
        var focus = target.FocusHandle;
        if (focus == 0 || focus == target.WindowHandle || !NativeMethods.IsWindow(focus) || !NativeMethods.IsChild(target.WindowHandle, focus))
        {
            return;
        }

        var targetThread = NativeMethods.GetWindowThreadProcessId(target.WindowHandle, out _);
        var info = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (NativeMethods.GetGUIThreadInfo(targetThread, ref info) && info.hwndFocus == focus)
        {
            return;
        }

        WithAttachedInput(target.WindowHandle, () => NativeMethods.SetFocus(focus));
        log.Debug("Focused control restored.");
    }

    /// <summary>Temporarily shares input state with the thread owning <paramref name="window"/>.</summary>
    private static void WithAttachedInput(nint window, Action action)
    {
        var otherThread = NativeMethods.GetWindowThreadProcessId(window, out _);
        var ownThread = NativeMethods.GetCurrentThreadId();
        var attached = otherThread != 0 && otherThread != ownThread && NativeMethods.AttachThreadInput(ownThread, otherThread, true);
        try
        {
            action();
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(ownThread, otherThread, false);
            }
        }
    }
}
