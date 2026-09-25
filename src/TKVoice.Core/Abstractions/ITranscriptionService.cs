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

    /// <summary>
    /// Finalizes the audio so far as a segment (at a speech pause) so it is transcribed while the
    /// user keeps speaking. No-op if nothing was appended since the last segment.
    /// </summary>
    void CommitSegment();

    /// <summary>Signals end of audio and waits for the final transcript of everything appended.</summary>
    Task<string> CompleteAsync(CancellationToken cancellationToken);
}

/// <param name="isTransient">Network problems, timeouts, rate limits and server errors; worth retrying (FR-035).</param>
public sealed class TranscriptionException(string message, bool isTransient = false, Exception? inner = null) : Exception(message, inner)
{
    public bool IsTransient { get; } = isTransient;
}
