using System.Windows.Threading;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Processing;

namespace TKVoice.App.FlowBar;

/// <summary>Thread-safe <see cref="IUserNotifier"/> facade over the Flow Bar window (OverlayService).</summary>
internal sealed class FlowBarOverlay(FlowBarWindow window) : IUserNotifier
{
    private readonly Dispatcher _dispatcher = window.Dispatcher;

    public void SetState(DictationState state) => _dispatcher.BeginInvoke(() => window.SetState(state));

    public void ReportAudioLevel(double level) => _dispatcher.BeginInvoke(DispatcherPriority.Render, () => window.ReportAudioLevel(level));

    public void ShowError(string message) => _dispatcher.BeginInvoke(() => window.ShowError(message));

    public void ShowModeChanged(ProcessingMode mode) => _dispatcher.BeginInvoke(() => window.ShowMessage(
        mode == ProcessingMode.Smart ? "Smart Mode" : "Raw Mode – ohne Nachbearbeitung",
        isError: false));
}
