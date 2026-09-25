namespace TKVoice.Core.Abstractions;

public enum DictationState
{
    Idle,
    Recording,
    Processing,
}

/// <summary>User-facing status and messages. Implemented by the UI layer.</summary>
public interface IUserNotifier
{
    void SetState(DictationState state);
    void ShowError(string message);
}
