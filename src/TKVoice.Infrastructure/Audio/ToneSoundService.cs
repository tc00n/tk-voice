using NAudio.Wave;
using TKVoice.Core.Abstractions;

namespace TKVoice.Infrastructure.Audio;

/// <summary>
/// Plays short synthesized two-note cues: rising for start, falling for stop. Generated in memory,
/// so there are no sound files to ship, and scaled by the configured volume.
/// </summary>
public sealed class ToneSoundService : ISoundService
{
    private const int SampleRate = 44_100;
    private readonly byte[] _started;
    private readonly byte[] _stopped;
    private readonly bool _enabled;
    private readonly ILog _log;

    public ToneSoundService(bool enabled, double volume, ILog log)
    {
        _enabled = enabled && volume > 0;
        _log = log;
        var amplitude = Math.Clamp(volume, 0, 1) * 0.5;
        _started = Render([(660, 0.055), (990, 0.075)], amplitude);
        _stopped = Render([(990, 0.055), (660, 0.075)], amplitude);
    }

    public void PlayRecordingStarted() => Play(_started);

    public void PlayRecordingStopped() => Play(_stopped);

    private void Play(byte[] pcm)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            var output = new WaveOut();
            output.Init(new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(SampleRate, 16, 1)));
            output.PlaybackStopped += (_, _) => Task.Run(output.Dispose);
            output.Play();
        }
        catch (Exception ex)
        {
            // A missing output device must not break dictation.
            _log.Warn($"Sound playback failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>Sine notes with short fade in/out to avoid clicks.</summary>
    private static byte[] Render(IEnumerable<(double Frequency, double Seconds)> notes, double amplitude)
    {
        var samples = new List<short>();
        foreach (var (frequency, seconds) in notes)
        {
            var count = (int)(SampleRate * seconds);
            var fade = (int)(SampleRate * 0.008);
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Min(1, Math.Min(i, count - 1 - i) / (double)fade);
                var value = Math.Sin(2 * Math.PI * frequency * i / SampleRate) * amplitude * envelope;
                samples.Add((short)(value * short.MaxValue));
            }
        }

        var bytes = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
