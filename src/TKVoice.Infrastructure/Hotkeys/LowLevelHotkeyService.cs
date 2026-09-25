using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Hotkeys;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.Hotkeys;

/// <summary>
/// Global hotkeys via a low-level keyboard hook, which (unlike RegisterHotKey) reports key releases
/// as needed for push-to-talk. The hook runs on its own message-loop thread and only enqueues key
/// events; matching and event dispatch happen on a separate worker thread so the hook never stalls
/// system-wide keyboard input. Keys are passed through, never swallowed.
/// </summary>
public sealed class LowLevelHotkeyService : IHotkeyService
{
    private static readonly TimeSpan StuckKeyCheckInterval = TimeSpan.FromMilliseconds(250);

    private readonly ILog _log;
    private readonly HotkeyMatcher _matcher = new();
    private readonly HashSet<int> _suspectedStuckKeys = new();
    private readonly BlockingCollection<KeyEvent> _events = new();
    private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
    private Thread? _hookThread;
    private Thread? _dispatchThread;
    private uint _hookThreadId;
    private Timer? _stuckKeyTimer;

    public LowLevelHotkeyService(ILog log)
    {
        _log = log;
        _hookProc = HookCallback;
    }

    public event EventHandler<HotkeyAction>? Pressed;
    public event EventHandler<HotkeyAction>? Released;

    public void Register(HotkeyAction action, HotkeyGesture gesture)
    {
        _events.Add(new KeyEvent.Register(action, gesture));
    }

    public void Start()
    {
        var hookReady = new ManualResetEventSlim();
        Exception? hookError = null;

        _dispatchThread = new Thread(DispatchLoop) { IsBackground = true, Name = "TKVoice hotkey dispatch" };
        _dispatchThread.Start();

        _hookThread = new Thread(() =>
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            var hook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _hookProc, NativeMethods.GetModuleHandleW(null), 0);
            if (hook == 0)
            {
                hookError = new InvalidOperationException($"Keyboard hook could not be installed (Win32 error {Marshal.GetLastWin32Error()}).");
                hookReady.Set();
                return;
            }

            hookReady.Set();
            while (NativeMethods.GetMessageW(out _, 0, 0, 0) > 0)
            {
            }

            NativeMethods.UnhookWindowsHookEx(hook);
        })
        { IsBackground = true, Name = "TKVoice keyboard hook" };
        _hookThread.Start();

        hookReady.Wait();
        if (hookError is not null)
        {
            throw hookError;
        }

        _stuckKeyTimer = new Timer(_ => _events.Add(new KeyEvent.VerifyPhysicalState()), null, StuckKeyCheckInterval, StuckKeyCheckInterval);
        _log.Info("Global keyboard hook installed.");
    }

    public void Dispose()
    {
        _stuckKeyTimer?.Dispose();
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessageW(_hookThreadId, NativeMethods.WM_QUIT, 0, 0);
        }

        _events.CompleteAdding();
        _hookThread?.Join(TimeSpan.FromSeconds(1));
        _dispatchThread?.Join(TimeSpan.FromSeconds(1));
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

            // Ignore synthetic input, including TK Voice's own text insertion.
            if ((data.flags & NativeMethods.LLKHF_INJECTED) == 0)
            {
                var message = (int)wParam;
                var isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
                var isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    _events.TryAdd(new KeyEvent.Key((int)data.vkCode, isDown));
                }
            }
        }

        return NativeMethods.CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void DispatchLoop()
    {
        foreach (var keyEvent in _events.GetConsumingEnumerable())
        {
            try
            {
                switch (keyEvent)
                {
                    case KeyEvent.Register register:
                        _matcher.Register(register.Action, register.Gesture);
                        _log.Info($"Hotkey {register.Action} = {register.Gesture}.");
                        break;

                    case KeyEvent.Key key:
                        Raise(_matcher.OnKey(key.VirtualKey, key.IsDown));
                        break;

                    case KeyEvent.VerifyPhysicalState:
                        ReleaseKeysNoLongerHeld();
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.Error("Hotkey handler failed.", ex);
            }
        }
    }

    /// <summary>
    /// A key-up can be missed, e.g. when the secure desktop (UAC, Ctrl+Alt+Del) takes over.
    /// Without this check push-to-talk would keep recording until the key is pressed again.
    /// A key must look released in two consecutive checks, because the hook sees a key-down before
    /// GetAsyncKeyState reflects it.
    /// </summary>
    private void ReleaseKeysNoLongerHeld()
    {
        if (_matcher.ActiveActions.Count == 0)
        {
            _suspectedStuckKeys.Clear();
            return;
        }

        foreach (var key in _matcher.PressedKeys.ToArray())
        {
            if (NativeMethods.IsKeyPhysicallyDown(key))
            {
                _suspectedStuckKeys.Remove(key);
            }
            else if (!_suspectedStuckKeys.Add(key))
            {
                _suspectedStuckKeys.Remove(key);
                _log.Debug($"Missed key-up for VK 0x{key:X2}; releasing.");
                Raise(_matcher.OnKey(key, isDown: false));
            }
        }
    }

    private void Raise(IReadOnlyList<(HotkeyAction Action, bool Pressed)> transitions)
    {
        foreach (var (action, pressed) in transitions)
        {
            (pressed ? Pressed : Released)?.Invoke(this, action);
        }
    }

    private abstract record KeyEvent
    {
        public sealed record Key(int VirtualKey, bool IsDown) : KeyEvent;

        public sealed record Register(HotkeyAction Action, HotkeyGesture Gesture) : KeyEvent;

        public sealed record VerifyPhysicalState : KeyEvent;
    }
}
