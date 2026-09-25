using System.Text;
using System.Threading.Channels;
using TKVoice.Core.Abstractions;

namespace TKVoice.OpenAI.Realtime;

/// <summary>
/// One dictation's streaming transcription. Audio is queued immediately and sent once the
/// WebSocket is connected, so recording never waits for the network. Turn detection is off:
/// the client commits once at the end of the recording and waits for the final transcript.
/// Text is assembled from every item the server reports, in order of first appearance, so a
/// server-side split into several items cannot drop speech. Completed transcripts win over deltas.
/// </summary>
internal sealed class RealtimeTranscriptionSession : ITranscriptionSession
{
    private readonly IRealtimeTransport _transport;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _trailingSilence;
    private readonly ILog _log;
    private readonly Channel<string> _outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<string> _final = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private readonly List<string> _itemOrder = [];
    private readonly HashSet<string> _committedItems = [];
    private readonly Dictionary<string, string> _completedItems = [];
    private readonly Dictionary<string, StringBuilder> _deltaText = [];
    private long _audioBytesAppended;
    private bool _commitRequested;
    private bool _commitWasEmpty;
    private bool _audioAppended;
    private Task _run = Task.CompletedTask;

    public RealtimeTranscriptionSession(
        IRealtimeTransport transport,
        TranscriptionSessionOptions options,
        TimeSpan connectTimeout,
        TimeSpan trailingSilence,
        ILog log)
    {
        _transport = transport;
        _connectTimeout = connectTimeout;
        _trailingSilence = trailingSilence;
        _log = log;
        _outgoing.Writer.TryWrite(RealtimeProtocol.SessionUpdate(options));
    }

    public void Start() => _run = Task.Run(RunAsync);

    public void AppendAudio(ReadOnlyMemory<byte> pcm)
    {
        if (!pcm.IsEmpty)
        {
            Volatile.Write(ref _audioAppended, true);
            Interlocked.Add(ref _audioBytesAppended, pcm.Length);
            _outgoing.Writer.TryWrite(RealtimeProtocol.AppendAudio(pcm.Span));
        }
    }

    public async Task<string> CompleteAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _commitRequested = true;
        }

        // Recording stops the instant the hotkey is released. A short silent tail gives the model
        // context to finalize a word that was still being spoken.
        if (Volatile.Read(ref _audioAppended) && _trailingSilence > TimeSpan.Zero)
        {
            var silenceBytes = (int)(_trailingSilence.TotalSeconds * AudioFormat.BytesPerSecond) & ~1;
            _outgoing.Writer.TryWrite(RealtimeProtocol.AppendAudio(new byte[silenceBytes]));
        }

        _log.Debug($"Committing {Interlocked.Read(ref _audioBytesAppended)} bytes of audio plus {_trailingSilence.TotalMilliseconds:F0} ms silence.");
        _outgoing.Writer.TryWrite(RealtimeProtocol.Commit());
        _outgoing.Writer.TryComplete();
        return await _final.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _outgoing.Writer.TryComplete();
        _final.TrySetCanceled();
        await _cts.CancelAsync();

        try
        {
            await _run;
        }
        catch (Exception)
        {
            // Failures were already surfaced through the final transcript task.
        }

        await _transport.DisposeAsync();
        _cts.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
            {
                connectTimeout.CancelAfter(_connectTimeout);
                var started = DateTimeOffset.UtcNow;
                await _transport.ConnectAsync(connectTimeout.Token);
                _log.Info($"Realtime transcription connected in {(DateTimeOffset.UtcNow - started).TotalMilliseconds:F0} ms.");
            }

            var receive = ReceiveLoopAsync(_cts.Token);
            var send = SendLoopAsync(_cts.Token);
            await Task.WhenAll(send, receive);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Disposed.
        }
        catch (Exception ex)
        {
            Fail(new TranscriptionException("Verbindung zur OpenAI-Transkription fehlgeschlagen.", ex));
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _outgoing.Reader.ReadAllAsync(cancellationToken))
        {
            await _transport.SendAsync(message, cancellationToken);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!_final.Task.IsCompleted)
        {
            var message = await _transport.ReceiveAsync(cancellationToken);
            if (message is null)
            {
                Fail(new TranscriptionException("Die Verbindung zur OpenAI-Transkription wurde unerwartet geschlossen."));
                return;
            }

            Handle(RealtimeProtocol.Parse(message));
        }
    }

    private void Handle(ServerEvent serverEvent)
    {
        switch (serverEvent)
        {
            case ServerEvent.Committed committed:
                _log.Debug($"Realtime event: committed item={committed.ItemId}.");
                lock (_gate)
                {
                    Track(committed.ItemId);
                    _committedItems.Add(committed.ItemId);
                    TryFinish();
                }

                break;

            case ServerEvent.TranscriptDelta delta:
                lock (_gate)
                {
                    Track(delta.ItemId);
                    if (!_deltaText.TryGetValue(delta.ItemId, out var text))
                    {
                        _deltaText[delta.ItemId] = text = new StringBuilder();
                        _log.Debug($"Realtime event: first delta item={delta.ItemId}.");
                    }

                    text.Append(delta.Delta);
                }

                break;

            case ServerEvent.TranscriptCompleted completed:
                lock (_gate)
                {
                    Track(completed.ItemId);
                    _completedItems[completed.ItemId] = completed.Transcript;
                    _log.Debug($"Realtime event: completed item={completed.ItemId} chars={completed.Transcript.Length} " +
                               $"deltaChars={(_deltaText.TryGetValue(completed.ItemId, out var d) ? d.Length : 0)}.");
                    TryFinish();
                }

                break;

            case ServerEvent.Error { Code: RealtimeProtocol.CommitEmptyErrorCode }:
                _log.Debug("Realtime event: commit was empty.");
                lock (_gate)
                {
                    _commitWasEmpty = true;
                    TryFinish();
                }

                break;

            case ServerEvent.Error error:
                _log.Warn($"Realtime API error: {error.Code}.");
                Fail(new TranscriptionException($"OpenAI-Fehler: {error.Message} ({error.Code})"));
                break;

            case ServerEvent.Other other:
                _log.Debug($"Realtime event: {other.Type}.");
                break;
        }
    }

    private void Track(string itemId)
    {
        if (!_itemOrder.Contains(itemId))
        {
            _itemOrder.Add(itemId);
        }
    }

    /// <summary>Resolves the final transcript once every committed item has completed. Caller holds the lock.</summary>
    private void TryFinish()
    {
        if (!_commitRequested)
        {
            return;
        }

        var allCommittedCompleted = _committedItems.Count > 0 && _committedItems.All(_completedItems.ContainsKey);
        if (!_commitWasEmpty && !allCommittedCompleted)
        {
            return;
        }

        var parts = _itemOrder
            .Select(TextOf)
            .Where(text => text.Length > 0);
        var result = string.Join(" ", parts);
        if (result.Length == 0)
        {
            _log.Warn($"Empty transcript: items={_itemOrder.Count}, committed={_committedItems.Count}, " +
                      $"completed={_completedItems.Count}, audioBytes={Interlocked.Read(ref _audioBytesAppended)}.");
        }

        _final.TrySetResult(result);
    }

    private string TextOf(string itemId)
    {
        if (_completedItems.TryGetValue(itemId, out var completed) && completed.Trim().Length > 0)
        {
            return completed.Trim();
        }

        return _deltaText.TryGetValue(itemId, out var deltas) ? deltas.ToString().Trim() : string.Empty;
    }

    private void Fail(Exception exception) => _final.TrySetException(exception);
}
