using TKVoice.Core.Dictation;

namespace TKVoice.Tests.Dictation;

public class SpeechActivityTrackerTests
{
    private static readonly TimeSpan Chunk = TimeSpan.FromMilliseconds(50);

    private static SpeechActivityTracker Create(int silenceTimeoutSeconds = 0) =>
        new(0.3, TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(silenceTimeoutSeconds));

    private static List<SpeechActivity> Feed(SpeechActivityTracker tracker, double level, TimeSpan duration) =>
        Enumerable.Range(0, (int)(duration / Chunk)).Select(_ => tracker.OnChunk(level, Chunk)).ToList();

    [Fact]
    public void Commits_segment_at_pause_after_minimum_length()
    {
        var tracker = Create();

        Assert.DoesNotContain(Feed(tracker, 0.8, TimeSpan.FromSeconds(12)), a => a.CommitSegment);
        var pause = Feed(tracker, 0.05, TimeSpan.FromSeconds(1));

        Assert.Single(pause, a => a.CommitSegment);
    }

    [Fact]
    public void No_segment_before_minimum_length()
    {
        var tracker = Create();

        Feed(tracker, 0.8, TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(Feed(tracker, 0.05, TimeSpan.FromSeconds(2)), a => a.CommitSegment);
    }

    [Fact]
    public void No_segment_for_pure_silence()
    {
        var tracker = Create();

        Assert.DoesNotContain(Feed(tracker, 0.05, TimeSpan.FromSeconds(30)), a => a.CommitSegment);
    }

    [Fact]
    public void Silence_timeout_after_configured_seconds_without_speech()
    {
        var tracker = Create(silenceTimeoutSeconds: 5);

        Feed(tracker, 0.8, TimeSpan.FromSeconds(2));
        var silence = Feed(tracker, 0.05, TimeSpan.FromSeconds(6));

        Assert.False(silence[(int)(TimeSpan.FromSeconds(4.9) / Chunk)].SilenceTimeoutReached);
        Assert.True(silence[^1].SilenceTimeoutReached);
    }

    [Fact]
    public void Speech_resets_silence_timeout_and_zero_disables_it()
    {
        var tracker = Create(silenceTimeoutSeconds: 5);
        Feed(tracker, 0.05, TimeSpan.FromSeconds(4));
        Feed(tracker, 0.8, TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(Feed(tracker, 0.05, TimeSpan.FromSeconds(4)), a => a.SilenceTimeoutReached);

        var disabled = Create(silenceTimeoutSeconds: 0);
        Assert.DoesNotContain(Feed(disabled, 0.05, TimeSpan.FromMinutes(2)), a => a.SilenceTimeoutReached);
    }
}
