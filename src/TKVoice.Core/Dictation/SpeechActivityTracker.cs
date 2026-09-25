namespace TKVoice.Core.Dictation;

/// <summary>
/// Level-based speech/pause tracking over the audio stream of one dictation. Decides when a pause
/// allows committing a transcription segment, and when the hands-free silence timeout is reached.
/// Not thread-safe; fed from the audio capture thread.
/// </summary>
public sealed class SpeechActivityTracker(
    double speechThreshold,
    TimeSpan segmentPause,
    TimeSpan minSegment,
    TimeSpan silenceTimeout)
{
    private TimeSpan _silence;
    private TimeSpan _segmentLength;
    private bool _speechInSegment;

    /// <param name="level">Input level of the chunk, 0..1.</param>
    /// <param name="duration">Audio duration of the chunk.</param>
    public SpeechActivity OnChunk(double level, TimeSpan duration)
    {
        _segmentLength += duration;
        if (level >= speechThreshold)
        {
            _silence = TimeSpan.Zero;
            _speechInSegment = true;
        }
        else
        {
            _silence += duration;
        }

        var commitSegment = _speechInSegment && _silence >= segmentPause && _segmentLength >= minSegment;
        if (commitSegment)
        {
            _segmentLength = TimeSpan.Zero;
            _speechInSegment = false;
        }

        var silenceTimeoutReached = silenceTimeout > TimeSpan.Zero && _silence >= silenceTimeout;
        return new SpeechActivity(commitSegment, silenceTimeoutReached);
    }
}

public readonly record struct SpeechActivity(bool CommitSegment, bool SilenceTimeoutReached);
