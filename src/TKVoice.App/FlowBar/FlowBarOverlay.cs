using System.Windows.Threading;
using TKVoice.Core.Abstractions;

namespace TKVoice.App.FlowBar;

/// <summary>Thread-safe <see cref="IUserNotifier"/> facade over the Flow Bar window (OverlayService).</summary>
internal sealed class FlowBarOverlay(FlowBarWindow window) : IUserNotifier
{
    private readonly Dispatcher _dispatcher = window.Dispatcher;

    public void SetState(DictationState state) => _dispatcher.BeginInvoke(() => window.SetState(state));

    public void ReportAudioLevel(double level) => _dispatcher.BeginInvoke(DispatcherPriority.Render, () => window.ReportAudioLevel(level));

    public void ShowError(string message) => _dispatcher.BeginInvoke(() => window.ShowError(message));
}
