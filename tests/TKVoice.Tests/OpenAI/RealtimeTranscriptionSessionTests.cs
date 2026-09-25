using System.Text.Json;
using System.Threading.Channels;
using TKVoice.Core.Abstractions;
using TKVoice.OpenAI.Realtime;

namespace TKVoice.Tests.OpenAI;

public class RealtimeTranscriptionSessionTests
{
    private static readonly TranscriptionSessionOptions Options = new("gpt-live-transcribe", "low", ["de"], [], null);

    [Fact]
    public async Task Sends_session_update_first_then_buffered_audio_then_commit()
    {
        var transport = new FakeTransport();
        await using var session = Start(transport);

        session.AppendAudio(new byte[] { 1, 2 });
        var completion = session.CompleteAsync(CancellationToken.None);

        await transport.WaitForSentAsync("input_audio_buffer.commit");
        Assert.Equal(["session.update", "input_audio_buffer.append", "input_audio_buffer.append", "input_audio_buffer.commit"], transport.SentTypes);
        Assert.Equal(300 * 48, transport.AppendedBytes[^1]); // trailing silence: 300 ms at 48 000 bytes/s

        transport.Receive("""{"type":"input_audio_buffer.committed","item_id":"item_1","previous_item_id":null}""");
        transport.Receive("""{"type":"conversation.item.input_audio_transcription.completed","item_id":"item_1","transcript":" Hallo Welt. "}""");

        Assert.Equal("Hallo Welt.", await completion);
    }

    [Fact]
    public async Task Completed_before_committed_still_resolves()
    {
        var transport = new FakeTransport();
        await using var session = Start(transport);
        session.AppendAudio(new byte[] { 1 });
        var completion = session.CompleteAsync(CancellationToken.None);

        transport.Receive("""{"type":"conversation.item.input_audio_transcription.completed","item_id":"item_1","transcript":"Test"}""");
        transport.Receive("""{"type":"input_audio_buffer.committed","item_id":"item_1"}""");

        Assert.Equal("Test", await completion);
    }

    [Fact]
    public async Task No_trailing_silence_without_recorded_audio()
    {
        var transport = new FakeTransport();
        await using var session = Start(transport);
        _ = session.CompleteAsync(CancellationToken.None);

        await transport.WaitForSentAsync("input_audio_buffer.commit");
        Assert.Equal(["session.update", "input_audio_buffer.commit"], transport.SentTypes);
    }

    [Fact]
    public async Task Empty_commit_yields_empty_transcript()
    {
        var transport = new FakeTransport();
        await using var session = Start(transport);
        var completion = session.CompleteAsync(CancellationToken.None);

        transport.Receive("""{"type":"error","error":{"code":"input_audio_buffer_commit_empty","message":"empty"}}""");

        Assert.Equal(string.Empty, await completion);
    }

    [Fact]
    public async Task Api_error_fails_the_transcript()
    {
        var transport = new FakeTransport();
        await using var session = Start(transport);
        var completion = session.CompleteAsync(CancellationToken.None);

        transport.Receive("""{"type":"error","error":{"code":"invalid_api_key","message":"Incorrect API key"}}""");

        var ex = await Assert.ThrowsAsync<TranscriptionException>(() => completion);
        Assert.Contains("invalid_api_key", ex.Message);
    }

    [Fact]
    public async Task Closed_connection_fails_the_transcript()
    {
        var transport = new FakeTransport();
        await using var session = Start(transport);
        var completion = session.CompleteAsync(CancellationToken.None);

        transport.Close();

        await Assert.ThrowsAsync<TranscriptionException>(() => completion);
    }

    [Fact]
    public async Task Connect_failure_fails_the_transcript()
    {
        var transport = new FakeTransport { ConnectError = new IOException("offline") };
        await using var session = Start(transport);

        await Assert.ThrowsAsync<TranscriptionException>(() => session.CompleteAsync(CancellationToken.None));
    }

    private static RealtimeTranscriptionSession Start(FakeTransport transport)
    {
        var session = new RealtimeTranscriptionSession(transport, Options, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300), new NullLog());
        session.Start();
        return session;
    }

    private sealed class FakeTransport : IRealtimeTransport
    {
        private readonly Channel<string?> _incoming = Channel.CreateUnbounded<string?>();
        private readonly List<string> _sent = [];
        private readonly SemaphoreSlim _sentSignal = new(0);

        public Exception? ConnectError { get; init; }

        public List<string> SentTypes
        {
            get
            {
                lock (_sent)
                {
                    return _sent.Select(m => JsonDocument.Parse(m).RootElement.GetProperty("type").GetString()!).ToList();
                }
            }
        }

        public List<int> AppendedBytes
        {
            get
            {
                lock (_sent)
                {
                    return _sent
                        .Select(m => JsonDocument.Parse(m).RootElement)
                        .Where(e => e.GetProperty("type").GetString() == "input_audio_buffer.append")
                        .Select(e => Convert.FromBase64String(e.GetProperty("audio").GetString()!).Length)
                        .ToList();
                }
            }
        }

        public void Receive(string json) => _incoming.Writer.TryWrite(json);

        public void Close() => _incoming.Writer.TryWrite(null);

        public async Task WaitForSentAsync(string type)
        {
            while (!SentTypes.Contains(type))
            {
                await _sentSignal.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        public Task ConnectAsync(CancellationToken cancellationToken) =>
            ConnectError is null ? Task.CompletedTask : Task.FromException(ConnectError);

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            lock (_sent)
            {
                _sent.Add(message);
            }

            _sentSignal.Release();
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) =>
            await _incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
