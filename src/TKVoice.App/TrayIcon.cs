using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using TKVoice.Core.Abstractions;
using TKVoice.Infrastructure;
using Application = System.Windows.Application;

namespace TKVoice.App;

/// <summary>
/// System tray presence (FR-043, minimal for now). Until the Flow Bar exists, the icon color is the
/// visible recording indicator required by §47.
/// </summary>
internal sealed class TrayIcon : IUserNotifier, IDisposable
{
    private readonly ICredentialService _credentials;
    private readonly ILog _log;
    private readonly NotifyIcon _notifyIcon;
    private readonly Dictionary<DictationState, Icon> _icons;

    public TrayIcon(ICredentialService credentials, ILog log)
    {
        _credentials = credentials;
        _log = log;
        _icons = new Dictionary<DictationState, Icon>
        {
            [DictationState.Idle] = CreateIcon(Color.FromArgb(0x33, 0x66, 0xCC)),
            [DictationState.Recording] = CreateIcon(Color.FromArgb(0xD9, 0x30, 0x25)),
            [DictationState.Processing] = CreateIcon(Color.FromArgb(0xE8, 0xA3, 0x17)),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("OpenAI API Key hinterlegen …", null, (_, _) => PromptForApiKey());
        menu.Items.Add("Logs öffnen", null, (_, _) => OpenLogs());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Application.Current.Shutdown());

        _notifyIcon = new NotifyIcon
        {
            Icon = _icons[DictationState.Idle],
            Text = "TK Voice",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    public void SetState(DictationState state) => OnUiThread(() =>
    {
        _notifyIcon.Icon = _icons[state];
        _notifyIcon.Text = state switch
        {
            DictationState.Recording => "TK Voice – Aufnahme",
            DictationState.Processing => "TK Voice – Verarbeitung",
            _ => "TK Voice",
        };
    });

    public void ShowError(string message) => OnUiThread(() => _notifyIcon.ShowBalloonTip(5000, "TK Voice", message, ToolTipIcon.Warning));

    public void ShowInfo(string message) => OnUiThread(() => _notifyIcon.ShowBalloonTip(3000, "TK Voice", message, ToolTipIcon.Info));

    public void PromptForApiKey() => OnUiThread(() =>
    {
        var dialog = new ApiKeyWindow();
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _credentials.SetOpenAIApiKey(dialog.ApiKey);
                _log.Info("OpenAI API key stored in Windows Credential Manager.");
                ShowInfo("API Key gespeichert.");
            }
            catch (Exception ex)
            {
                _log.Error("Storing API key failed.", ex);
                ShowError(ex.Message);
            }
        }
    });

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }

    private static void OpenLogs()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Process.Start(new ProcessStartInfo(AppPaths.LogDirectory) { UseShellExecute = true });
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
