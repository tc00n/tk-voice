namespace TKVoice.Core.Abstractions;

/// <summary>
/// Captures microphone audio as 16-bit mono PCM at <see cref="AudioFormat.SampleRate"/>.
/// The microphone must only be open between <see cref="Start"/> and <see cref="Stop"/>.
/// </summary>
public interface IAudioCaptureService
{
    /// <summary>Opens the microphone and delivers PCM chunks to <paramref name="onChunk"/> on a capture thread.</summary>
    void Start(Action<ReadOnlyMemory<byte>> onChunk);

    /// <summary>Stops capturing and releases the microphone. Returns once no further chunks will be delivered.</summary>
    void Stop();

    /// <summary>
    /// Raised on a capture thread when recording ends unexpectedly, e.g. the microphone was unplugged.
    /// Handlers must not call <see cref="Stop"/> synchronously.
    /// </summary>
    event EventHandler<Exception>? Failed;
}

public static class AudioFormat
{
    public const int SampleRate = 24_000;
    public const int BitsPerSample = 16;
    public const int Channels = 1;
    public const int BytesPerSecond = SampleRate * Channels * BitsPerSample / 8;

    public static TimeSpan DurationOf(long byteCount) => TimeSpan.FromSeconds((double)byteCount / BytesPerSecond);
}
