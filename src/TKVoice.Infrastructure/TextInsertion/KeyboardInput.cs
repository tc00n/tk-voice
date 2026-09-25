using System.Runtime.InteropServices;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.TextInsertion;

/// <summary>Synthesized keyboard input via SendInput.</summary>
internal static class KeyboardInput
{
    private const int CharactersPerBatch = 200;

    private static readonly int[] ModifierKeys = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];

    /// <summary>
    /// Waits until the user has released Shift/Ctrl/Alt/Win, which would otherwise combine with
    /// synthesized keys (e.g. Ctrl+Shift+C opens developer tools). Returns false on timeout.
    /// </summary>
    public static async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (ModifierKeys.Any(NativeMethods.IsKeyPhysicallyDown))
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(10, cancellationToken);
        }

        return true;
    }

    public static void Copy() => Send(
    [
        VirtualKey(NativeMethods.VK_CONTROL, keyUp: false),
        VirtualKey(NativeMethods.VK_C, keyUp: false),
        VirtualKey(NativeMethods.VK_C, keyUp: true),
        VirtualKey(NativeMethods.VK_CONTROL, keyUp: true),
    ]);

    public static void Paste() => Send(
    [
        VirtualKey(NativeMethods.VK_CONTROL, keyUp: false),
        VirtualKey(NativeMethods.VK_V, keyUp: false),
        VirtualKey(NativeMethods.VK_V, keyUp: true),
        VirtualKey(NativeMethods.VK_CONTROL, keyUp: true),
    ]);

    /// <summary>
    /// Injects an unassigned key. Windows only lets the process that received the last input event
    /// change the foreground window; this makes TK Voice that process without affecting any app.
    /// </summary>
    public static void TapUnassignedKey() => Send(
    [
        VirtualKey(NativeMethods.VK_UNASSIGNED, keyUp: false),
        VirtualKey(NativeMethods.VK_UNASSIGNED, keyUp: true),
    ]);

    public static void Type(string text)
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

            Send(inputs);
        }
    }

    private static unsafe void Send(NativeMethods.INPUT[] inputs)
    {
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
