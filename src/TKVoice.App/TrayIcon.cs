using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Processing;
using TKVoice.Infrastructure;
using Application = System.Windows.Application;

namespace TKVoice.App;

/// <summary>
/// System tray presence (FR-043): activate/pause, open settings, show and switch Smart/Raw, quit.
/// The icon color also mirrors the dictation state (blue ready, red recording, orange processing,
/// grey paused).
/// </summary>
internal sealed class TrayIcon : IUserNotifier, IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Dictionary<DictationState, Icon> _icons;
    private readonly Icon _pausedIcon;
    private readonly TrayMenu _menu;
    private readonly ProcessingModeState _mode;
    private DictationState _state = DictationState.Idle;
    private bool _active = true;

    public TrayIcon(ProcessingModeState mode, ILog log)
    {
        _icons = new Dictionary<DictationState, Icon>
        {
            [DictationState.Idle] = CreateIcon(Color.FromArgb(0x33, 0x66, 0xCC)),
            [DictationState.Recording] = CreateIcon(Color.FromArgb(0xD9, 0x30, 0x25)),
            [DictationState.Processing] = CreateIcon(Color.FromArgb(0xE8, 0xA3, 0x17)),
        };
        _pausedIcon = CreateIcon(Color.FromArgb(0x8A, 0x8A, 0x8A));

        _mode = mode;
        _menu = new TrayMenu(() => App.Current.SetActive(!_active), () => App.Current.ToggleMode());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icons[DictationState.Idle],
            Text = "TK Voice",
            Visible = true,
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                _menu.Show(_active, _mode.Current == ProcessingMode.Smart);
            }
        };
        _notifyIcon.DoubleClick += (_, _) => App.Current.OpenSettings();
    }

    public void SetState(DictationState state) => OnUiThread(() =>
    {
        _state = state;
        UpdateIcon();
    });

    public void SetActive(bool active) => OnUiThread(() =>
    {
        _active = active;
        UpdateIcon();
    });

    public void ShowError(string message) => OnUiThread(() => _notifyIcon.ShowBalloonTip(5000, "TK Voice", message, ToolTipIcon.Warning));

    public void ShowInfo(string message) => OnUiThread(() => _notifyIcon.ShowBalloonTip(3000, "TK Voice", message, ToolTipIcon.Info));

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _pausedIcon.Dispose();
    }

    private void UpdateIcon()
    {
        if (!_active && _state == DictationState.Idle)
        {
            _notifyIcon.Icon = _pausedIcon;
            _notifyIcon.Text = "TK Voice – pausiert";
            return;
        }

        _notifyIcon.Icon = _icons[_state];
        _notifyIcon.Text = _state switch
        {
            DictationState.Recording => "TK Voice – Aufnahme",
            DictationState.Processing => "TK Voice – Verarbeitung",
            _ => "TK Voice",
        };
    }

    internal static void OpenLogs(ILog log)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo(AppPaths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log.Error("Opening the log folder failed.", ex);
        }
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private static Icon CreateIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 2, 2, 28, 28);
            using var font = new Font("Segoe UI", 11, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(graphics, "TK", font, new Rectangle(0, 0, 32, 32), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        var handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone();
        DestroyIcon(handle);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);
}
