using TKVoice.Core.Abstractions;
using TKVoice.Infrastructure.TextInsertion;

namespace TKVoice.Infrastructure.Clipboard;

/// <summary>
/// Reads the user's selection by sending Ctrl+C to the foreground application, then puts the
/// previous clipboard content back (FR-020, FR-029). Works in practically every application.
/// Only runs on an explicit hotkey press; nothing is read in the background (§47).
/// </summary>
public sealed class ClipboardSelectionReader(Win32ClipboardService clipboard, ILog log) : ISelectionReader
{
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(800);

    public async Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken)
    {
        // The hotkey's own modifiers (e.g. Ctrl+Shift) would turn Ctrl+C into Ctrl+Shift+C.
        if (!await KeyboardInput.WaitForModifiersReleasedAsync(ModifierReleaseTimeout, cancellationToken))
        {
            log.Warn("Modifier keys still held; selection not read.");
            return null;
        }

        var snapshot = clipboard.Capture();
        var before = Win32ClipboardService.SequenceNumber;
        KeyboardInput.Copy();

        var deadline = DateTimeOffset.UtcNow + CopyTimeout;
        while (Win32ClipboardService.SequenceNumber == before)
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                // Nothing selected, or the application does not support Ctrl+C.
                return null;
            }

            await Task.Delay(15, cancellationToken);
        }

        // Some applications write several formats one after another; let them finish.
        await Task.Delay(50, cancellationToken);
        var afterCopy = Win32ClipboardService.SequenceNumber;
        var text = clipboard.ReadText();

        if (!clipboard.RestoreIfSequenceUnchanged(snapshot, afterCopy))
        {
            log.Warn("Clipboard changed again after copying the selection; previous content not restored.");
        }

        return text;
    }
}
