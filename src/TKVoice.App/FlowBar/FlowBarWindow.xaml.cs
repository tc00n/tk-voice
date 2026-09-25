using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TKVoice.Core.Abstractions;
using Screen = System.Windows.Forms.Screen;

namespace TKVoice.App.FlowBar;

/// <summary>
/// Small floating status pill (FR-031–FR-033). Never takes focus, ignores the mouse and is
/// shown bottom-center on the monitor of the dictation target. UI thread only.
/// </summary>
public partial class FlowBarWindow : Window
{
    private const int BarCount = 24;
    private const double MinBarHeight = 3;
    private const double MaxBarHeight = 24;
    private const int BottomMarginPixels = 24;
    private static readonly TimeSpan SlowProcessingThreshold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ErrorDisplayDuration = TimeSpan.FromSeconds(4);

    private static readonly Brush RecordingBrush = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Brush ProcessingBrush = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A));
    private static readonly Brush BarBrush = Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush InfoBrush = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));

    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _levels = new double[BarCount];
    private readonly DispatcherTimer _processingAnimation;
    private readonly DispatcherTimer _slowProcessing;
    private readonly DispatcherTimer _errorHide;
    private DictationState _state = DictationState.Idle;
    private Screen? _screen;
    private double _animationPhase;
    private string? _pendingInfo;
    private bool _handsFree;
    private DateTimeOffset _recordingStarted;
    private readonly DispatcherTimer _handsFreeClock;

    public FlowBarWindow()
    {
        InitializeComponent();

        for (var i = 0; i < BarCount; i++)
        {
            _bars[i] = new Rectangle
            {
                Width = 3,
                Height = MinBarHeight,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Margin = new Thickness(1, 0, 1, 0),
                Fill = BarBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Bars.Children.Add(_bars[i]);
        }

        _processingAnimation = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, (_, _) => AnimateProcessing(), Dispatcher);
        _processingAnimation.Stop();
        _slowProcessing = new DispatcherTimer(SlowProcessingThreshold, DispatcherPriority.Normal, (_, _) => ShowSlowHint(), Dispatcher);
        _slowProcessing.Stop();
        _errorHide = new DispatcherTimer(ErrorDisplayDuration, DispatcherPriority.Normal, (_, _) => HideAfterError(), Dispatcher);
        _errorHide.Stop();
        _handsFreeClock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => ShowHandsFreeStatus(), Dispatcher);
        _handsFreeClock.Stop();

        SizeChanged += (_, _) => Reposition();

        // Create the native window up front so the first dictation shows the bar without delay.
        new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>Hands-free recordings show a label with elapsed time, since no key is being held.</summary>
    public void SetHandsFree(bool handsFree)
    {
        _handsFree = handsFree && _state == DictationState.Recording;
        if (_handsFree)
        {
            ShowHandsFreeStatus();
            _handsFreeClock.Start();
        }
        else
        {
            _handsFreeClock.Stop();
        }
    }

    public void SetState(DictationState state)
    {
        _state = state;
        if (state != DictationState.Recording)
        {
            _handsFree = false;
            _handsFreeClock.Stop();
        }

        switch (state)
        {
            case DictationState.Recording:
                _errorHide.Stop();
                _pendingInfo = null;
                _processingAnimation.Stop();
                _slowProcessing.Stop();
                Array.Clear(_levels);
                RenderLevels();
                Dot.Fill = RecordingBrush;
                Bars.Visibility = Visibility.Visible;
                _recordingStarted = DateTimeOffset.Now;
                SetStatus(null);
                ShowOnTargetMonitor();
                break;

            case DictationState.Processing:
                Dot.Fill = ProcessingBrush;
                SetStatus(null);
                _animationPhase = 0;
                _processingAnimation.Start();
                _slowProcessing.Start();
                break;

            case DictationState.Idle:
                _processingAnimation.Stop();
                _slowProcessing.Stop();
                var pending = _pendingInfo;
                _pendingInfo = null;
                if (_errorHide.IsEnabled)
                {
                    // An error from this dictation is showing; it takes precedence.
                }
                else if (pending is not null)
                {
                    ShowMessage(pending, isError: false);
                }
                else
                {
                    Hide();
                }

                break;
        }
    }

    public void ReportAudioLevel(double level)
    {
        if (_state != DictationState.Recording)
        {
            return;
        }

        Array.Copy(_levels, 1, _levels, 0, BarCount - 1);
        _levels[^1] = level;
        RenderLevels();
    }

    public void ShowError(string message) => ShowMessage(message, isError: true);

    /// <summary>Shows a short message for a few seconds, e.g. an error or a mode change.</summary>
    public void ShowMessage(string message, bool isError)
    {
        if (_state != DictationState.Idle && !isError)
        {
            // Never cover the recording/processing display; show it once the bar is free again.
            _pendingInfo = message;
            return;
        }

        _processingAnimation.Stop();
        _slowProcessing.Stop();
        Dot.Fill = isError ? RecordingBrush : InfoBrush;
        Bars.Visibility = Visibility.Collapsed;
        SetStatus(message);
        if (!IsVisible)
        {
            ShowOnTargetMonitor();
        }

        _errorHide.Stop();
        _errorHide.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
    }

    private void RenderLevels()
    {
        for (var i = 0; i < BarCount; i++)
        {
            _bars[i].Height = MinBarHeight + _levels[i] * (MaxBarHeight - MinBarHeight);
        }
    }

    private void AnimateProcessing()
    {
        _animationPhase += 0.25;
        for (var i = 0; i < BarCount; i++)
        {
            var wave = (Math.Sin(_animationPhase - i * 0.45) + 1) / 2;
            _levels[i] = 0.15 + wave * 0.35;
        }

        RenderLevels();
    }

    private void ShowHandsFreeStatus()
    {
        if (_handsFree)
        {
            var elapsed = DateTimeOffset.Now - _recordingStarted;
            SetStatus($"Freihändig · {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}");
        }
    }

    private void ShowSlowHint()
    {
        _slowProcessing.Stop();
        if (_state == DictationState.Processing)
        {
            SetStatus("Verarbeitung dauert länger …");
        }
    }

    private void HideAfterError()
    {
        _errorHide.Stop();
        if (_state == DictationState.Idle)
        {
            Hide();
        }
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text ?? string.Empty;
        StatusText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowOnTargetMonitor()
    {
        _screen = Screen.FromHandle(GetForegroundWindow());
        if (!IsVisible)
        {
            Show();
        }

        Reposition();
    }

    /// <summary>Positions in physical pixels so it is correct on any monitor DPI.</summary>
    private void Reposition()
    {
        if (!IsVisible || _screen is null || PresentationSource.FromVisual(this) is not { CompositionTarget: { } target })
        {
            return;
        }

        var toDevice = target.TransformToDevice;
        var widthPx = (int)Math.Round(ActualWidth * toDevice.M11);
        var heightPx = (int)Math.Round(ActualHeight * toDevice.M22);
        var area = _screen.WorkingArea;
        var x = area.Left + (area.Width - widthPx) / 2;
        var y = area.Bottom - heightPx - BottomMarginPixels;

        SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_NOACTIVATE = 0x08000000;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const nint WS_EX_TRANSPARENT = 0x00000020;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = -1;

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
