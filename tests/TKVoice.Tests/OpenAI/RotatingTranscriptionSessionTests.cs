using TKVoice.Core.Abstractions;
using TKVoice.OpenAI.Realtime;

namespace TKVoice.Tests.OpenAI;

public class RotatingTranscriptionSessionTests
{
    private readonly List<FakeSession> _sessions = [];
    private DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private RotatingTranscriptionSession Create() => new(
        () =>
        {
            var session = new FakeSession($"Teil {_sessions.Count + 1}.");
            _sessions.Add(session);
            return session;
        },
        TimeSpan.FromMinutes(50),
        TimeSpan.FromMinutes(55),
        new NullLog(),
        () => _now);

    [Fact]
    public async Task Short_dictation_uses_a_single_session()
    {
        await using var session = Create();
        session.AppendAudio(new byte[] { 1 });
        session.CommitSegment();

        Assert.Equal("Teil 1.", await session.CompleteAsync(CancellationToken.None));
        Assert.Single(_sessions);
    }

    [Fact]
    public async Task Rotates_at_the_first_pause_after_rotation_time_and_joins_text_in_order()
    {
        await using var session = Create();
        session.AppendAudio(new byte[] { 1 });

        _now += TimeSpan.FromMinutes(51);
        session.AppendAudio(new byte[] { 2 });
        Assert.Single(_sessions); // no pause yet

        session.CommitSegment();
        Assert.Equal(2, _sessions.Count);
        Assert.True(_sessions[0].Completed);

        session.AppendAudio(new byte[] { 3 });
        Assert.Equal(1, _sessions[1].AppendCount);
        Assert.Equal("Teil 1. Teil 2.", await session.CompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rotates_without_pause_shortly_before_the_provider_limit()
    {
        await using var session = Create();
        session.AppendAudio(new byte[] { 1 });

        _now += TimeSpan.FromMinutes(56);
        session.AppendAudio(new byte[] { 2 });

        Assert.Equal(2, _sessions.Count);
        Assert.Equal("Teil 1. Teil 2.", await session.CompleteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Dispose_disposes_every_session()
    {
        var session = Create();
        _now += TimeSpan.FromMinutes(51);
        session.CommitSegment();

        await session.DisposeAsync();

        Assert.All(_sessions, s => Assert.True(s.Disposed));
    }

    private sealed class FakeSession(string text) : ITranscriptionSession
    {
        public int AppendCount { get; private set; }
        public bool Completed { get; private set; }
        public bool Disposed { get; private set; }

        public void AppendAudio(ReadOnlyMemory<byte> pcm) => AppendCount++;

        public void CommitSegment()
        {
        }

        public Task<string> CompleteAsync(CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.FromResult(text);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
