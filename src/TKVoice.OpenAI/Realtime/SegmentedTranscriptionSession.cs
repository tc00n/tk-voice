using TKVoice.Core.Abstractions;
using TKVoice.Core.Reliability;

namespace TKVoice.OpenAI.Realtime;

/// <summary>
/// Resilient transcription of a dictation of any length (§45, FR-035–FR-037).
///
/// Each segment (speech up to a pause) runs in its own realtime session, so no session gets near
/// the provider's 60-minute limit. A segment's audio is held only until its transcript has arrived.
/// If the session fails — network drop, timeout, server error — the segment is transcribed again in
/// a fresh session from the held audio, up to the configured number of retries. The audio is
/// released as soon as the segment has succeeded or finally failed.
/// </summary>
internal sealed class SegmentedTranscriptionSession : ITranscriptionSession
{
    private readonly Func<ITranscriptionSession> _createSession;
    private readonly int _maxRetries;
    private readonly TimeSpan _attemptTimeout;
    private readonly TimeSpan _maxSegmentDuration;
    private readonly ILog _log;
    private readonly Func<int, TimeSpan>? _backoff;
    private readonly CancellationTokenSource _disposed = new();
    private readonly Lock _gate = new();
    private readonly List<Task<string>> _closedSegments = [];
    private Segment? _current;
    private int _segmentCount;

    public SegmentedTranscriptionSession(
        Func<ITranscriptionSession> createSession,
        int maxRetries,
        TimeSpan attemptTimeout,
        TimeSpan maxSegmentDuration,
        ILog log,
        Func<int, TimeSpan>? backoff = null)
    {
        _createSession = createSession;
        _maxRetries = maxRetries;
        _attemptTimeout = attemptTimeout;
        _maxSegmentDuration = maxSegmentDuration;
        _log = log;
        _backoff = backoff;
        _current = new Segment(createSession());
    }

    public void AppendAudio(ReadOnlyMemory<byte> pcm)
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return;
            }

            if (_current.Duration >= _maxSegmentDuration)
            {
                // No pause for a very long time; splitting may cut a word, which beats hitting the session limit.
                CloseCurrentSegment();
            }

            _current!.Append(pcm);
        }
    }

    public void CommitSegment()
    {
        lock (_gate)
        {
            if (_current is { HasAudio: true })
            {
                CloseCurrentSegment();
            }
        }
    }

    public async Task<string> CompleteAsync(CancellationToken cancellationToken)
    {
        Task<string>[] segments;
        Segment? unused = null;
        lock (_gate)
        {
            if (_current is { HasAudio: true })
            {
                _closedSegments.Add(TranscribeAsync(_current, ++_segmentCount));
            }
            else
            {
                unused = _current;
            }

            _current = null;
            segments = [.. _closedSegments];
        }

        if (unused is not null)
        {
            await unused.Session.DisposeAsync();
        }

        var texts = await Task.WhenAll(segments).WaitAsync(cancellationToken);
        return string.Join(" ", texts.Select(t => t.Trim()).Where(t => t.Length > 0));
    }

    public async ValueTask DisposeAsync()
    {
        await _disposed.CancelAsync();

        Task<string>[] segments;
        Segment? current;
        lock (_gate)
        {
            segments = [.. _closedSegments];
            current = _current;
            _current = null;
        }

        if (current is not null)
        {
            await current.Session.DisposeAsync();
        }

        foreach (var segment in segments)
        {
            try
            {
                await segment;
            }
            catch (Exception)
            {
                // Already reported through CompleteAsync, or no longer of interest.
            }
        }

        _disposed.Dispose();
    }

    /// <summary>Caller holds the lock.</summary>
    private void CloseCurrentSegment()
    {
        _closedSegments.Add(TranscribeAsync(_current!, ++_segmentCount));
        _current = new Segment(_createSession());
    }

    private async Task<string> TranscribeAsync(Segment segment, int number)
    {
        try
        {
            return await Retry.RunAsync(
                $"Transcription of segment {number}",
                async (attempt, token) =>
                {
                    if (attempt > 0)
                    {
                        await segment.Session.DisposeAsync();
                        segment.Session = _createSession();
                        segment.Replay();
                        _log.Info($"Segment {number}: retry {attempt} with {segment.Duration.TotalSeconds:F1} s of held audio.");
                    }

                    return await segment.Session.CompleteAsync(token);
                },
                _maxRetries,
                _attemptTimeout,
                ex => ex is TranscriptionException { IsTransient: true },
                _log,
                _disposed.Token,
                _backoff);
        }
        catch (TimeoutException ex)
        {
            throw new TranscriptionException("Die OpenAI-Transkription antwortet nicht. " + ex.Message, isTransient: true, ex);
        }
        finally
        {
            // FR-037: temporary audio is deleted after success or final failure.
            segment.ReleaseAudio();
            await segment.Session.DisposeAsync();
        }
    }

    private sealed class Segment(ITranscriptionSession session)
    {
        private List<ReadOnlyMemory<byte>>? _audio = [];
        private long _bytes;

        public ITranscriptionSession Session { get; set; } = session;

        public bool HasAudio => _bytes > 0;

        public TimeSpan Duration => AudioFormat.DurationOf(_bytes);

        public void Append(ReadOnlyMemory<byte> pcm)
        {
            _audio?.Add(pcm);
            _bytes += pcm.Length;
            Session.AppendAudio(pcm);
        }

        public void Replay()
        {
            foreach (var chunk in _audio ?? [])
            {
                Session.AppendAudio(chunk);
            }
        }

        public void ReleaseAudio() => _audio = null;
    }
}
