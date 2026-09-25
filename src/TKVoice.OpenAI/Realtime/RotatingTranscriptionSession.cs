using TKVoice.Core.Abstractions;

namespace TKVoice.OpenAI.Realtime;

/// <summary>
/// Chains realtime sessions so a dictation has no length limit (§45) although the provider ends a
/// session after 60 minutes. Rotation happens at a segment boundary (speech pause) once the current
/// session is old enough, or unconditionally shortly before the provider limit. Earlier sessions
/// finish in the background; the final text joins all sessions in order.
/// </summary>
internal sealed class RotatingTranscriptionSession : ITranscriptionSession
{
    private readonly Func<ITranscriptionSession> _createSession;
    private readonly TimeSpan _rotateAtPauseAfter;
    private readonly TimeSpan _rotateAlwaysAfter;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILog _log;
    private readonly Lock _gate = new();
    private readonly List<(ITranscriptionSession Session, Task<string> Text)> _finished = [];
    private ITranscriptionSession _current;
    private DateTimeOffset _currentStarted;

    public RotatingTranscriptionSession(
        Func<ITranscriptionSession> createSession,
        TimeSpan rotateAtPauseAfter,
        TimeSpan rotateAlwaysAfter,
        ILog log,
        Func<DateTimeOffset>? clock = null)
    {
        _createSession = createSession;
        _rotateAtPauseAfter = rotateAtPauseAfter;
        _rotateAlwaysAfter = rotateAlwaysAfter;
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _current = createSession();
        _currentStarted = _clock();
    }

    public void AppendAudio(ReadOnlyMemory<byte> pcm)
    {
        lock (_gate)
        {
            if (_clock() - _currentStarted >= _rotateAlwaysAfter)
            {
                // No pause for a long time; rotating now may split a word, which beats losing the session.
                Rotate();
            }

            _current.AppendAudio(pcm);
        }
    }

    public void CommitSegment()
    {
        lock (_gate)
        {
            _current.CommitSegment();
            if (_clock() - _currentStarted >= _rotateAtPauseAfter)
            {
                Rotate();
            }
        }
    }

    public async Task<string> CompleteAsync(CancellationToken cancellationToken)
    {
        ITranscriptionSession last;
        Task<string>[] earlier;
        lock (_gate)
        {
            last = _current;
            earlier = _finished.Select(f => f.Text).ToArray();
        }

        var lastText = await last.CompleteAsync(cancellationToken);
        var earlierTexts = await Task.WhenAll(earlier).WaitAsync(cancellationToken);
        return string.Join(" ", earlierTexts.Append(lastText).Select(t => t.Trim()).Where(t => t.Length > 0));
    }

    public async ValueTask DisposeAsync()
    {
        ITranscriptionSession[] sessions;
        lock (_gate)
        {
            sessions = [.. _finished.Select(f => f.Session), _current];
        }

        foreach (var session in sessions)
        {
            await session.DisposeAsync();
        }
    }

    /// <summary>Caller holds the lock.</summary>
    private void Rotate()
    {
        var previous = _current;
        _finished.Add((previous, previous.CompleteAsync(CancellationToken.None)));
        _current = _createSession();
        _currentStarted = _clock();
        _log.Info($"Transcription session rotated (session {_finished.Count + 1}).");
    }
}
