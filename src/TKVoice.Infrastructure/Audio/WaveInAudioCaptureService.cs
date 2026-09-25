using NAudio.Wave;
using TKVoice.Core.Abstractions;

namespace TKVoice.Infrastructure.Audio;

/// <summary>
/// Microphone capture via WinMM (NAudio WaveIn). Windows resamples to the requested
/// 24 kHz 16-bit mono format. The device is opened per dictation and closed right after (§47).
/// A watchdog reports a failure when no audio arrives for a while, since some devices simply go
/// silent instead of raising an error when they are unplugged.
/// </summary>
public sealed class WaveInAudioCaptureService(string deviceName, ILog log) : IAudioCaptureService
{
    private const int BufferMilliseconds = 50;
    private static readonly TimeSpan NoDataTimeout = TimeSpan.FromSeconds(2);

    private WaveIn? _waveIn;
    private Action<ReadOnlyMemory<byte>>? _onChunk;
    private ManualResetEventSlim? _stopped;
    private Timer? _watchdog;
    private long _lastDataTicks;
    private volatile bool _stopping;
    private int _failureRaised;

    public event EventHandler<Exception>? Failed;

    public void Start(Action<ReadOnlyMemory<byte>> onChunk)
    {
        if (_waveIn is not null)
        {
            throw new InvalidOperationException("Audio capture is already running.");
        }

        var deviceNumber = MicrophoneCatalog.Resolve(deviceName);
        if (deviceNumber == MicrophoneCatalog.DefaultDevice && !string.IsNullOrWhiteSpace(deviceName))
        {
            log.Warn("Configured microphone not found; using the Windows default microphone.");
        }

        _onChunk = onChunk;
        _stopped = new ManualResetEventSlim();
        _stopping = false;
        _failureRaised = 0;
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
        catch (Exception ex)
        {
            Cleanup();
            throw new InvalidOperationException(
                deviceNumber < 0
                    ? "Kein Mikrofon verfügbar. Bitte ein Aufnahmegerät anschließen oder in Windows als Standard festlegen."
                    : $"Das Mikrofon (Gerät {deviceNumber}) ist nicht verfügbar.",
                ex);
        }

        Interlocked.Exchange(ref _lastDataTicks, Environment.TickCount64);
        _watchdog = new Timer(_ => CheckForData(), null, NoDataTimeout, TimeSpan.FromMilliseconds(500));

        log.Debug($"Microphone opened (device {deviceNumber}).");
    }

    public void Stop()
    {
        if (_waveIn is null)
        {
            return;
        }

        _stopping = true;
        _watchdog?.Dispose();
        _watchdog = null;
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
            Interlocked.Exchange(ref _lastDataTicks, Environment.TickCount64);
            // The buffer is reused by NAudio, so hand out a copy.
            _onChunk?.Invoke(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopped?.Set();
        if (e.Exception is not null)
        {
            log.Error("Microphone stopped with an error.", e.Exception);
            RaiseFailed(e.Exception);
        }
        else if (!_stopping)
        {
            RaiseFailed(new InvalidOperationException("Recording stopped unexpectedly."));
        }
    }

    private void CheckForData()
    {
        var silentFor = TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastDataTicks));
        if (!_stopping && silentFor >= NoDataTimeout)
        {
            RaiseFailed(new TimeoutException($"No audio data for {silentFor.TotalSeconds:F1} s."));
        }
    }

    private void RaiseFailed(Exception exception)
    {
        if (Interlocked.Exchange(ref _failureRaised, 1) == 0)
        {
            Failed?.Invoke(this, exception);
        }
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
