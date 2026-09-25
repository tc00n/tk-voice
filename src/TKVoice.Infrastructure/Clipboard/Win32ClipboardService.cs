using System.Runtime.InteropServices;
using TKVoice.Core.Abstractions;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.Clipboard;

/// <summary>
/// Clipboard access for paste-based insertion with clipboard preservation (FR-029).
///
/// The dictated text is offered with delayed rendering: Windows asks TK Voice for the data at the
/// moment the target application actually reads it (WM_RENDERFORMAT). That tells us exactly when
/// the paste has consumed the text, so the previous clipboard content can be restored right after
/// without ever risking that a slow application pastes the old content instead.
///
/// All clipboard calls run on the UI thread, which owns a message-only window and pumps messages;
/// the clipboard owner must answer render requests promptly or other applications would hang.
/// Clipboard content is never logged or inspected beyond copying it back (§47).
/// </summary>
public sealed class Win32ClipboardService : IDisposable
{
    private const string WindowClassName = "TKVoice.ClipboardOwner";
    private const int WM_RENDERFORMAT = 0x0305;
    private const int WM_RENDERALLFORMATS = 0x0306;
    private const int WM_DESTROYCLIPBOARD = 0x0307;
    private const int OpenAttempts = 10;
    private const long MaxFormatBytes = 64L * 1024 * 1024;

    private readonly SynchronizationContext _uiContext;
    private readonly ILog _log;
    private readonly WndProc _wndProc;
    private readonly uint _excludeFromMonitoringFormat;
    private readonly uint _canIncludeInHistoryFormat;
    private readonly uint _canUploadToCloudFormat;
    private nint _hwnd;

    // UI thread only.
    private string? _pendingText;
    private TaskCompletionSource? _pendingRead;

    public Win32ClipboardService(SynchronizationContext uiContext, ILog log)
    {
        _uiContext = uiContext;
        _log = log;
        _wndProc = WindowProc;
        _excludeFromMonitoringFormat = NativeMethods.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
        _canIncludeInHistoryFormat = NativeMethods.RegisterClipboardFormatW("CanIncludeInClipboardHistory");
        _canUploadToCloudFormat = NativeMethods.RegisterClipboardFormatW("CanUploadToCloudClipboard");
        OnUiThread(CreateOwnerWindow);
    }

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Copies every memory-based clipboard format so it can be put back later.</summary>
    public ClipboardSnapshot Capture() => OnUiThread(() =>
    {
        using var _ = OpenClipboard();
        var formats = new List<(uint, byte[])>();
        for (var format = NativeMethods.EnumClipboardFormats(0); format != 0; format = NativeMethods.EnumClipboardFormats(format))
        {
            if (!IsMemoryBackedFormat(format))
            {
                continue;
            }

            var handle = NativeMethods.GetClipboardData(format);
            var data = handle == 0 ? null : CopyFromGlobal(handle);
            if (data is not null)
            {
                formats.Add((format, data));
            }
        }

        return new ClipboardSnapshot(formats);
    });

    /// <summary>
    /// Offers <paramref name="text"/> on the clipboard, excluded from clipboard history and cloud sync.
    /// The returned task completes when an application reads the text.
    /// </summary>
    public Task OfferTextForPaste(string text) => OnUiThread(() =>
    {
        using (OpenClipboard())
        {
            NativeMethods.EmptyClipboard();

            // Set after EmptyClipboard, whose WM_DESTROYCLIPBOARD would clear it if we were the previous owner.
            _pendingText = text;
            _pendingRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, 0); // delayed rendering
            SetHistoryExclusion();
        }

        return _pendingRead.Task;
    });

    /// <summary>
    /// Puts the snapshot back, unless someone else has replaced the clipboard in the meantime
    /// (then their newer content wins). Returns whether the snapshot was restored.
    /// </summary>
    public bool RestoreIfStillOwner(ClipboardSnapshot snapshot) => OnUiThread(() =>
    {
        _pendingText = null;
        _pendingRead = null;
        if (NativeMethods.GetClipboardOwner() != _hwnd)
        {
            return false;
        }

        using (OpenClipboard())
        {
            NativeMethods.EmptyClipboard();
            foreach (var (format, data) in snapshot.Formats)
            {
                SetGlobal(format, data);
            }

            // The original content is already in the clipboard history; don't add a duplicate.
            if (snapshot.Formats.All(f => f.Format != _canIncludeInHistoryFormat))
            {
                SetHistoryExclusion();
            }
        }

        return true;
    });

    public void Dispose()
    {
        if (_hwnd != 0)
        {
            OnUiThread(() => NativeMethods.DestroyWindow(_hwnd));
            _hwnd = 0;
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_RENDERFORMAT when (uint)wParam == NativeMethods.CF_UNICODETEXT:
                // The clipboard is already open by the reading application; must not open it here.
                if (_pendingText is not null)
                {
                    SetGlobal(NativeMethods.CF_UNICODETEXT, TextBytes(_pendingText));
                }

                _pendingRead?.TrySetResult();
                return 0;

            case WM_RENDERALLFORMATS:
                if (_pendingText is not null && NativeMethods.OpenClipboard(hWnd))
                {
                    if (NativeMethods.GetClipboardOwner() == hWnd)
                    {
                        SetGlobal(NativeMethods.CF_UNICODETEXT, TextBytes(_pendingText));
                    }

                    NativeMethods.CloseClipboard();
                }

                return 0;

            case WM_DESTROYCLIPBOARD:
                _pendingText = null;
                return 0;

            default:
                return DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    private void CreateOwnerWindow()
    {
        var windowClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = WindowClassName,
        };

        if (RegisterClassExW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
        {
            throw new InvalidOperationException($"Clipboard window class could not be registered (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        _hwnd = NativeMethods.CreateWindowExW(0, WindowClassName, null, 0, 0, 0, 0, 0, NativeMethods.HWND_MESSAGE, 0, windowClass.hInstance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"Clipboard window could not be created (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    private ClipboardLease OpenClipboard()
    {
        for (var attempt = 1; attempt <= OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(_hwnd))
            {
                return new ClipboardLease();
            }

            Thread.Sleep(20);
        }

        throw new ClipboardUnavailableException("Die Zwischenablage ist durch ein anderes Programm blockiert.");
    }

    private void SetHistoryExclusion()
    {
        byte[] zero = [0, 0, 0, 0];
        SetGlobal(_excludeFromMonitoringFormat, zero);
        SetGlobal(_canIncludeInHistoryFormat, zero);
        SetGlobal(_canUploadToCloudFormat, zero);
    }

    private static byte[] TextBytes(string text) => System.Text.Encoding.Unicode.GetBytes(text + '\0');

    private static void SetGlobal(uint format, byte[] data)
    {
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (nuint)Math.Max(data.Length, 1));
        if (handle == 0)
        {
            return;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        Marshal.Copy(data, 0, pointer, data.Length);
        NativeMethods.GlobalUnlock(handle);

        // On success the system owns the memory.
        if (NativeMethods.SetClipboardData(format, handle) == 0)
        {
            NativeMethods.GlobalFree(handle);
        }
    }

    private byte[]? CopyFromGlobal(nint handle)
    {
        var size = (long)NativeMethods.GlobalSize(handle);
        if (size <= 0 || size > MaxFormatBytes)
        {
            if (size > MaxFormatBytes)
            {
                _log.Warn($"Clipboard format larger than {MaxFormatBytes / (1024 * 1024)} MB skipped during preservation.");
            }

            return null;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            var data = new byte[size];
            Marshal.Copy(pointer, data, 0, data.Length);
            return data;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>Formats whose handle is not an HGLOBAL (GDI objects, metafiles, owner-display, private).</summary>
    private static bool IsMemoryBackedFormat(uint format) => format switch
    {
        2 or 3 or 9 or 14 => false, // CF_BITMAP, CF_METAFILEPICT, CF_PALETTE, CF_ENHMETAFILE (CF_DIB is kept)
        0x80 or 0x82 or 0x83 or 0x8E => false, // owner display and DSP variants
        >= 0x200 and <= 0x3FF => false, // CF_PRIVATEFIRST..CF_GDIOBJLAST
        _ => true,
    };

    private T OnUiThread<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        _uiContext.Send(_ =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }, null);

        if (error is not null)
        {
            throw error;
        }

        return result;
    }

    private void OnUiThread(Action action) => OnUiThread(() =>
    {
        action();
        return true;
    });

    private readonly struct ClipboardLease : IDisposable
    {
        public void Dispose() => NativeMethods.CloseClipboard();
    }

    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);
}

public sealed record ClipboardSnapshot(IReadOnlyList<(uint Format, byte[] Data)> Formats);

public sealed class ClipboardUnavailableException(string message) : Exception(message);
