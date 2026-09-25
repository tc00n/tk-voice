using TKVoice.Core.Abstractions;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;

namespace TKVoice.Core.Dictation;

/// <summary>
/// Orchestrates one dictation at a time: capture target → record/stream → final transcript →
/// Smart or Raw processing → insert.
/// Hotkey callbacks arrive sequentially; processing runs asynchronously so hotkeys stay responsive.
/// </summary>
public sealed class DictationController
{
    private readonly IAudioCaptureService _audio;
    private readonly ITranscriptionService _transcription;
    private readonly ISmartTextProcessor _smartProcessor;
    private readonly ProcessingModeState _mode;
    private readonly ITargetCaptureService _targetCapture;
    private readonly ITextInsertionService _insertion;
    private readonly IUserNotifier _notifier;
    private readonly ISoundService _sounds;
    private readonly ILog _log;
    private readonly TKVoiceSettings _settings;
    private readonly Lock _gate = new();

    private DictationState _state = DictationState.Idle;
    private ActiveDictation? _current;

    public DictationController(
        IAudioCaptureService audio,
        ITranscriptionService transcription,
        ISmartTextProcessor smartProcessor,
        ProcessingModeState mode,
        ITargetCaptureService targetCapture,
        ITextInsertionService insertion,
        IUserNotifier notifier,
        ISoundService sounds,
        ILog log,
        TKVoiceSettings settings)
    {
        _audio = audio;
        _transcription = transcription;
        _smartProcessor = smartProcessor;
        _mode = mode;
        _targetCapture = targetCapture;
        _insertion = insertion;
        _notifier = notifier;
        _sounds = sounds;
        _log = log;
        _settings = settings;
    }

    public DictationState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Completes when the most recently started processing run has finished. Intended for tests.</summary>
    public Task ProcessingCompletion { get; private set; } = Task.CompletedTask;

    public void OnHotkeyPressed(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.PushToTalk:
                StartRecording();
                break;

            case HotkeyAction.ToggleSmartRaw:
                var mode = _mode.Toggle();
                _log.Info($"Processing mode switched to {mode}.");
                _notifier.ShowModeChanged(mode);
                break;
        }
    }

    public void OnHotkeyReleased(HotkeyAction action)
    {
        if (action == HotkeyAction.PushToTalk)
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        lock (_gate)
        {
            if (_state != DictationState.Idle)
            {
                _log.Debug($"Hotkey ignored in state {_state}.");
                return;
            }

            var target = _targetCapture.CaptureCurrentTarget();
            if (target is null)
            {
                _log.Warn("No focused target window; dictation not started.");
                return;
            }

            ITranscriptionSession session;
            try
            {
                session = _transcription.StartSession();
            }
            catch (Exception ex)
            {
                _log.Error("Could not start transcription session.", ex);
                _notifier.ShowError(ex.Message);
                return;
            }

            var dictation = new ActiveDictation(target, session, _mode.Current, _notifier);
            try
            {
                _audio.Start(dictation.OnAudio);
            }
            catch (Exception ex)
            {
                _log.Error("Could not start microphone.", ex);
                _notifier.ShowError("Mikrofon konnte nicht gestartet werden.");
                _ = session.DisposeAsync().AsTask();
                return;
            }

            _current = dictation;
            _state = DictationState.Recording;
            _sounds.PlayRecordingStarted();
            if (dictation.Mode == ProcessingMode.Smart)
            {
                _smartProcessor.Warmup();
            }
            _log.Info($"Recording started ({dictation.Mode}). Target: {target}.");
        }

        _notifier.SetState(DictationState.Recording);
    }

    private void StopRecording()
    {
        ActiveDictation dictation;
        lock (_gate)
        {
            if (_state != DictationState.Recording || _current is null)
            {
                return;
            }

            _audio.Stop();
            _sounds.PlayRecordingStopped();
            dictation = _current;
            _current = null;
            _state = DictationState.Processing;
            ProcessingCompletion = Task.Run(() => ProcessAsync(dictation));
        }

        _log.Info($"Recording stopped after {dictation.AudioDuration.TotalSeconds:F1} s of audio.");
        _notifier.SetState(DictationState.Processing);
    }

    private async Task ProcessAsync(ActiveDictation dictation)
    {
        var stopped = DateTimeOffset.UtcNow;
        try
        {
            if (dictation.AudioDuration < TimeSpan.FromMilliseconds(_settings.Audio.MinimumRecordingMilliseconds))
            {
                _log.Info("Recording too short; discarded.");
                return;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.Processing.TimeoutSeconds));
            var transcript = (await dictation.Session.CompleteAsync(timeout.Token)).Trim();
            _log.Info($"Final transcript after {(DateTimeOffset.UtcNow - stopped).TotalMilliseconds:F0} ms ({transcript.Length} chars).");

            if (transcript.Length == 0)
            {
                return;
            }

            var text = await ApplyModeAsync(dictation, transcript);
            if (text.Length == 0)
            {
                return;
            }

            var result = await _insertion.InsertAsync(dictation.Target, text, CancellationToken.None);
            if (result == InsertionResult.TargetUnavailable)
            {
                _log.Warn($"Target unavailable; text discarded. Target: {dictation.Target}.");
                _notifier.ShowError("Das ursprüngliche Eingabefeld ist nicht mehr verfügbar. Der Text wurde nicht eingefügt.");
                return;
            }

            _log.Info($"Text inserted. Stop-to-insert latency: {(DateTimeOffset.UtcNow - stopped).TotalMilliseconds:F0} ms.");
        }
        catch (OperationCanceledException)
        {
            _log.Warn($"Processing timed out after {_settings.Processing.TimeoutSeconds} s.");
            _notifier.ShowError("Die Verarbeitung hat zu lange gedauert und wurde abgebrochen.");
        }
        catch (Exception ex)
        {
            _log.Error("Dictation processing failed.", ex);
            _notifier.ShowError($"Diktat fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            await dictation.Session.DisposeAsync();

            lock (_gate)
            {
                _state = DictationState.Idle;
            }

            _notifier.SetState(DictationState.Idle);
        }
    }

    /// <summary>
    /// Smart processing with graceful degradation: whatever goes wrong, the dictation is never lost;
    /// the raw transcript is inserted instead and the user is told.
    /// </summary>
    private async Task<string> ApplyModeAsync(ActiveDictation dictation, string transcript)
    {
        if (dictation.Mode == ProcessingMode.Raw)
        {
            return transcript;
        }

        if (_settings.Processing.SkipSmartForSimpleText && SmartSkipHeuristic.CanSkip(transcript))
        {
            _log.Info("Smart processing skipped: simple text.");
            return transcript;
        }

        var started = DateTimeOffset.UtcNow;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.Processing.SmartTimeoutSeconds));
            var request = new TextProcessingRequest(
                transcript,
                dictation.Target.ProcessName,
                _settings.Processing.SendWindowTitle ? dictation.Target.WindowTitle : null);
            var output = await _smartProcessor.ProcessAsync(request, timeout.Token);
            _log.Info($"Smart processing took {(DateTimeOffset.UtcNow - started).TotalMilliseconds:F0} ms ({transcript.Length} → {output.Length} chars).");

            if (output.Trim().Length == 0)
            {
                _log.Info("Smart processing found nothing to insert.");
                return string.Empty;
            }

            if (SmartOutputGuard.TryAccept(transcript, output, out var accepted))
            {
                return accepted;
            }

            _log.Warn("Smart output rejected by guard; inserting raw transcript.");
            _notifier.ShowError("Smart Mode lieferte eine unerwartete Antwort – Rohtext eingefügt.");
        }
        catch (Exception ex)
        {
            _log.Error($"Smart processing failed after {(DateTimeOffset.UtcNow - started).TotalMilliseconds:F0} ms; inserting raw transcript.", ex);
            _notifier.ShowError("Smart Mode nicht verfügbar – Rohtext eingefügt.");
        }

        return transcript;
    }

    private sealed class ActiveDictation(DictationTarget target, ITranscriptionSession session, ProcessingMode mode, IUserNotifier notifier)
    {
        private long _audioBytes;

        public DictationTarget Target { get; } = target;
        public ProcessingMode Mode { get; } = mode;
        public ITranscriptionSession Session { get; } = session;
        public TimeSpan AudioDuration => AudioFormat.DurationOf(Interlocked.Read(ref _audioBytes));

        public void OnAudio(ReadOnlyMemory<byte> pcm)
        {
            Interlocked.Add(ref _audioBytes, pcm.Length);
            Session.AppendAudio(pcm);
            notifier.ReportAudioLevel(AudioLevel.Compute(pcm.Span));
        }
    }
}
