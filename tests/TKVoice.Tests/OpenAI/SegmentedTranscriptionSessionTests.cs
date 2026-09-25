using TKVoice.Core.Abstractions;
using TKVoice.OpenAI.Realtime;

namespace TKVoice.Tests.OpenAI;

public class SegmentedTranscriptionSessionTests
{
    private readonly List<FakeSession> _sessions = [];
    private readonly Queue<Func<FakeSession, Task<string>>> _behaviors = new();

    private SegmentedTranscriptionSession Create(int maxRetries = 2, TimeSpan? maxSegment = null) => new(
        () =>
        {
            var behavior = _behaviors.Count > 0 ? _behaviors.Dequeue() : s => Task.FromResult($"Text{s.TotalBytes}");
            var session = new FakeSession(behavior);
            _sessions.Add(session);
            return session;
        },
        maxRetries,
        TimeSpan.FromSeconds(5),
        maxSegment ?? TimeSpan.FromMinutes(55),
        new NullLog(),
        _ => TimeSpan.Zero);

    [Fact]
    public async Task Segments_run_in_separate_sessions_and_join_in_order()
    {
        await using var session = Create();
        session.AppendAudio(new byte[10]);
        session.CommitSegment();
        session.AppendAudio(new byte[20]);

        Assert.Equal("Text10 Text20", await session.CompleteAsync(CancellationToken.None));
        Assert.Equal(2, _sessions.Count);
    }

    [Fact]
    public async Task Failed_segment_is_replayed_in_a_new_session()
    {
        _behaviors.Enqueue(_ => Task.FromException<string>(new TranscriptionException("Verbindung unterbrochen", isTransient: true)));
        await using var session = Create();
        session.AppendAudio(new byte[10]);
        session.AppendAudio(new byte[5]);

        Assert.Equal("Text15", await session.CompleteAsync(CancellationToken.None));
        Assert.Equal(2, _sessions.Count);
        Assert.Equal(15, _sessions[1].TotalBytes); // full audio of the segment was replayed
        Assert.All(_sessions, s => Assert.True(s.Disposed));
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        for (var i = 0; i < 3; i++)
        {
            _behaviors.Enqueue(_ => Task.FromException<string>(new TranscriptionException("offline", isTransient: true)));
        }

        await using var session = Create(maxRetries: 2);
        session.AppendAudio(new byte[10]);

        await Assert.ThrowsAsync<TranscriptionException>(() => session.CompleteAsync(CancellationToken.None));
        Assert.Equal(3, _sessions.Count);
    }

    [Fact]
    public async Task Configuration_errors_are_not_retried()
    {
        _behaviors.Enqueue(_ => Task.FromException<string>(new TranscriptionException("API Key ungültig", isTransient: false)));
        await using var session = Create();
        session.AppendAudio(new byte[10]);

        var ex = await Assert.ThrowsAsync<TranscriptionException>(() => session.CompleteAsync(CancellationToken.None));
        Assert.Contains("API Key", ex.Message);
        Assert.Single(_sessions);
    }

    [Fact]
    public async Task Hanging_session_times_out_and_is_retried()
    {
        _behaviors.Enqueue(_ => new TaskCompletionSource<string>().Task); // never answers
        var session = new SegmentedTranscriptionSession(
            () =>
            {
                var behavior = _behaviors.Count > 0 ? _behaviors.Dequeue() : s => Task.FromResult("Ok.");
                var fake = new FakeSession(behavior);
                _sessions.Add(fake);
                return fake;
            },
            maxRetries: 1,
            attemptTimeout: TimeSpan.FromMilliseconds(100),
            maxSegmentDuration: TimeSpan.FromMinutes(55),
            new NullLog(),
            _ => TimeSpan.Zero);
        await using var _ = session;
        session.AppendAudio(new byte[10]);

        Assert.Equal("Ok.", await session.CompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Very_long_segment_without_pause_is_split()
    {
        await using var session = Create(maxSegment: AudioFormat.DurationOf(100));
        session.AppendAudio(new byte[60]);
        session.AppendAudio(new byte[60]); // 120 bytes > limit after this one
        session.AppendAudio(new byte[10]); // starts a new segment

        Assert.Equal("Text120 Text10", await session.CompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task No_audio_means_empty_result()
    {
        await using var session = Create();
        Assert.Equal(string.Empty, await session.CompleteAsync(CancellationToken.None));
    }

    private sealed class FakeSession(Func<FakeSession, Task<string>> complete) : ITranscriptionSession
    {
        public int TotalBytes { get; private set; }
        public bool Disposed { get; private set; }

        public void AppendAudio(ReadOnlyMemory<byte> pcm) => TotalBytes += pcm.Length;

        public void CommitSegment()
        {
        }

        public Task<string> CompleteAsync(CancellationToken cancellationToken) => complete(this).WaitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
