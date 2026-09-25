using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using TKVoice.Core.Abstractions;

namespace TKVoice.OpenAI.Realtime;

internal sealed class WebSocketRealtimeTransport(Uri uri, string apiKey) : IRealtimeTransport
{
    private readonly ClientWebSocket _socket = new();

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
        _socket.Options.CollectHttpResponseDetails = true;
        try
        {
            await _socket.ConnectAsync(uri, cancellationToken);
        }
        catch (WebSocketException ex) when (_socket.HttpStatusCode != 0)
        {
            // The handshake was answered: e.g. 401 is a configuration problem, not a network one.
            var status = _socket.HttpStatusCode;
            throw new TranscriptionException(OpenAIErrors.UserMessage(null, null, status), OpenAIErrors.IsTransient(status), ex);
        }
    }

    public Task SendAsync(string message, CancellationToken cancellationToken) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            }
        }
        catch (Exception)
        {
            // Closing is best effort; the socket is disposed either way.
        }

        _socket.Dispose();
    }
}
