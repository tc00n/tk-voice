using NAudio.Wave;
using TKVoice.Core.Abstractions;

namespace TKVoice.Infrastructure.Audio;

/// <summary>
/// Microphone capture via WinMM (NAudio WaveIn). Windows resamples to the requested
/// 24 kHz 16-bit mono format. The device is opened per dictation and closed right after (§47).
/// </summary>
public sealed class WaveInAudioCaptureService(int deviceNumber, ILog log) : IAudioCaptureService
{
    private const int BufferMilliseconds = 50;

    private WaveIn? _waveIn;
    private Action<ReadOnlyMemory<byte>>? _onChunk;
    private ManualResetEventSlim? _stopped;

    public void Start(Action<ReadOnlyMemory<byte>> onChunk)
    {
        if (_waveIn is not null)
        {
            throw new InvalidOperationException("Audio capture is already running.");
        }

        _onChunk = onChunk;
        _stopped = new ManualResetEventSlim();
        _waveIn = new WaveIn
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels),
            BufferMilliseconds = BufferMilliseconds,
            NumberOfBuffers = 4,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        try
        {
            _waveIn.StartRecording();
        }
        catch
        {
            Cleanup();
            throw;
        }

        log.Debug($"Microphone opened (device {deviceNumber}).");
    }

    public void Stop()
    {
        if (_waveIn is null)
        {
            return;
        }

        _waveIn.StopRecording();

        // StopRecording returns before the final buffers are flushed; wait so the last words are not lost.
        if (!_stopped!.Wait(TimeSpan.FromSeconds(2)))
        {
            log.Warn("Microphone did not confirm stop within 2 s.");
        }

        Cleanup();
        log.Debug("Microphone closed.");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
        {
            // The buffer is reused by NAudio, so hand out a copy.
            _onChunk?.Invoke(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            log.Error("Microphone stopped with an error.", e.Exception);
        }

        _stopped?.Set();
    }

    private void Cleanup()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _onChunk = null;
        _stopped?.Dispose();
        _stopped = null;
    }
}
