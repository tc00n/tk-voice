using TKVoice.Core.Abstractions;

namespace TKVoice.App;

internal sealed class CompositeNotifier(params IUserNotifier[] notifiers) : IUserNotifier
{
    public void SetState(DictationState state)
    {
        foreach (var notifier in notifiers)
        {
            notifier.SetState(state);
        }
    }

    public void ReportAudioLevel(double level)
    {
        foreach (var notifier in notifiers)
        {
            notifier.ReportAudioLevel(level);
        }
    }

    public void ShowError(string message)
    {
        foreach (var notifier in notifiers)
        {
            notifier.ShowError(message);
        }
    }
}
