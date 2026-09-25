namespace TKVoice.Core.Abstractions;

/// <summary>Short audio cues for recording start and stop (FR-034). Must never block the caller.</summary>
public interface ISoundService
{
    void PlayRecordingStarted();
    void PlayRecordingStopped();
}
