namespace TKVoice.Core.Abstractions;

/// <summary>
/// Model-agnostic streaming speech-to-text. Implementations own model ids and wire protocols (§44).
/// </summary>
public interface ITranscriptionService
{
    /// <summary>Starts a session immediately; audio can be appended before the connection is established.</summary>
    ITranscriptionSession StartSession();
}

public interface ITranscriptionSession : IAsyncDisposable
{
    /// <summary>Queues a chunk of PCM audio. Thread-safe and non-blocking.</summary>
    void AppendAudio(ReadOnlyMemory<byte> pcm);

    /// <summary>Signals end of audio and waits for the final transcript of everything appended.</summary>
    Task<string> CompleteAsync(CancellationToken cancellationToken);
}

public sealed class TranscriptionException(string message, Exception? inner = null) : Exception(message, inner);
