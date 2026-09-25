using TKVoice.Core.Processing;

namespace TKVoice.Core.Abstractions;

public enum DictationState
{
    Idle,
    Recording,
    Processing,
}

/// <summary>User-facing status and messages. Implemented by the UI layer; may be called from any thread.</summary>
public interface IUserNotifier
{
    void SetState(DictationState state);

    /// <summary>Current input level while recording, 0 (silence) to 1 (loud).</summary>
    void ReportAudioLevel(double level)
    {
    }

    void ShowError(string message);

    void ShowModeChanged(ProcessingMode mode)
    {
    }
}
