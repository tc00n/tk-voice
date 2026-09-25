using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using TKVoice.App.Settings;

namespace TKVoice.App;

/// <summary>
/// Tray context menu as a WPF (Fluent-themed) menu instead of the dated WinForms one. A popup only
/// closes on outside clicks while its host is the foreground window, hence the invisible host window.
/// </summary>
internal sealed class TrayMenu
{
    private readonly Window _host;
    private readonly ContextMenu _menu;
    private readonly MenuItem _activeItem;
    private readonly MenuItem _smartItem;

    public TrayMenu(Action toggleActive, Action toggleMode)
    {
        _host = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
        };

        _activeItem = Item("TK Voice aktiv", "", (_, _) => toggleActive());
        _activeItem.IsCheckable = true;
        _smartItem = Item("Smart Mode", "", (_, _) => toggleMode());
        _smartItem.IsCheckable = true;

        _menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            Items =
            {
                Header("TK Voice"),
                _activeItem,
                _smartItem,
                new Separator(),
                Item("Einstellungen …", "", (_, _) => App.Current.OpenSettings()),
                Item("Wörterbuch …", "", (_, _) => App.Current.OpenSettings(SettingsPage.Dictionary)),
                Item("Logs öffnen", "", (_, _) => TrayIcon.OpenLogs(App.Current.Log)),
                new Separator(),
                Item("Beenden", "", (_, _) => Application.Current.Shutdown()),
            },
        };
        _menu.Closed += (_, _) => _host.Hide();
    }

    public void Show(bool active, bool smart)
    {
        _activeItem.IsChecked = active;
        _smartItem.IsChecked = smart;

        _host.Show();
        _host.Activate();
        SetForegroundWindow(new WindowInteropHelper(_host).Handle);
        _menu.PlacementTarget = _host;
        _menu.IsOpen = true;
    }

    private static MenuItem Item(string text, string glyph, RoutedEventHandler onClick)
    {
        var item = new MenuItem
        {
            Header = text,
            Icon = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 14 },
        };
        item.Click += onClick;
        return item;
    }

    private static MenuItem Header(string text) => new()
    {
        Header = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold },
        IsEnabled = false,
    };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
