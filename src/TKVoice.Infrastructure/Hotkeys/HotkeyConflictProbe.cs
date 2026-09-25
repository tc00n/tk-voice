using System.Runtime.InteropServices;
using TKVoice.Core.Hotkeys;

namespace TKVoice.Infrastructure.Hotkeys;

/// <summary>
/// Detects combinations already registered as global hotkeys by other applications (FR-001) by
/// briefly trying to register them. Only combinations of modifiers plus exactly one other key can
/// be checked this way; single keys and modifier-only gestures are reported as free.
/// </summary>
public static class HotkeyConflictProbe
{
    private const int ProbeId = 0x7A11;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    public static bool IsTakenByAnotherApplication(HotkeyGesture gesture)
    {
        uint modifiers = 0;
        int? key = null;
        foreach (var part in gesture.Parts)
        {
            var flag = ModifierFlag(part[0]);
            if (flag != 0)
            {
                modifiers |= flag;
            }
            else if (key is null && part.Length == 1)
            {
                key = part[0];
            }
            else
            {
                return false;
            }
        }

        if (key is null || modifiers == 0)
        {
            return false;
        }

        if (RegisterHotKey(0, ProbeId, modifiers | MOD_NOREPEAT, (uint)key.Value))
        {
            UnregisterHotKey(0, ProbeId);
            return false;
        }

        return Marshal.GetLastWin32Error() == ERROR_HOTKEY_ALREADY_REGISTERED;
    }

    private static uint ModifierFlag(int vk) => vk switch
    {
        0xA2 or 0xA3 => MOD_CONTROL,
        0xA0 or 0xA1 => MOD_SHIFT,
        0xA4 or 0xA5 => MOD_ALT,
        0x5B or 0x5C => MOD_WIN,
        _ => 0,
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
