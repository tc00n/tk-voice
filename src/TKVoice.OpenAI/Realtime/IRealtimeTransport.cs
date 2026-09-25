namespace TKVoice.OpenAI.Realtime;

/// <summary>Text-frame message transport for the Realtime API. Abstracted for testing.</summary>
internal interface IRealtimeTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>Returns the next complete message, or null once the connection is closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}
