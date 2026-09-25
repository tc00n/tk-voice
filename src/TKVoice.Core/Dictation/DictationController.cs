using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictionary;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;
using TKVoice.Core.Usage;

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
    private readonly PersonalDictionary _dictionary;
    private readonly ITargetCaptureService _targetCapture;
    private readonly ITextInsertionService _insertion;
    private readonly IUserNotifier _notifier;
    private readonly ISoundService _sounds;
    private readonly ILog _log;
    private readonly TKVoiceSettings _settings;
    private readonly UsageTracker? _usage;
    private readonly IPasswordFieldDetector _passwordFields;
    private readonly IDictationDebugLog _debugLog;
    private readonly Lock _gate = new();

    private DictationState _state = DictationState.Idle;
    private ActiveDictation? _current;

    public DictationController(
        IAudioCaptureService audio,
        ITranscriptionService transcription,
        ISmartTextProcessor smartProcessor,
        ProcessingModeState mode,
        PersonalDictionary dictionary,
        ITargetCaptureService targetCapture,
        ITextInsertionService insertion,
        IUserNotifier notifier,
        ISoundService sounds,
        ILog log,
        TKVoiceSettings settings,
        UsageTracker? usage = null,
        IPasswordFieldDetector? passwordFields = null,
        IDictationDebugLog? debugLog = null)
    {
        _usage = usage;
        _passwordFields = passwordFields ?? new NoPasswordFieldDetector();
        _debugLog = debugLog ?? new NoDictationDebugLog();
        _audio = audio;
        _audio.Failed += OnMicrophoneFailed;
        _transcription = transcription;
        _smartProcessor = smartProcessor;
        _mode = mode;
        _dictionary = dictionary;
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
                // In hands-free mode a tap on push-to-talk ends the recording.
                if (!StopHandsFree())
                {
                    StartRecording(handsFree: false);
                }

                break;

            case HotkeyAction.HandsFreeToggle:
                ToggleHandsFree();
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
        if (action != HotkeyAction.PushToTalk)
        {
            return;
        }

        ActiveDictation? pushToTalk;
        lock (_gate)
        {
            pushToTalk = _current is { HandsFree: false } ? _current : null;
        }

        if (pushToTalk is not null)
        {
            StopRecording(pushToTalk);
        }
    }

    /// <summary>
    /// Idle: start a hands-free dictation. Push-to-talk recording: lock it into hands-free, so it
    /// continues after the key is released. Hands-free recording: stop.
    /// </summary>
    private void ToggleHandsFree()
    {
        if (StopHandsFree())
        {
            return;
        }

        lock (_gate)
        {
            if (_current is { HandsFree: false } pushToTalk)
            {
                pushToTalk.HandsFree = true;
                _log.Info("Recording locked to hands-free.");
                _notifier.SetHandsFree(true);
                return;
            }
        }

        StartRecording(handsFree: true);
    }

    /// <summary>Stops the current recording if it is hands-free. Returns whether it did.</summary>
    private bool StopHandsFree()
    {
        ActiveDictation? handsFree;
        lock (_gate)
        {
            handsFree = _current is { HandsFree: true } ? _current : null;
        }

        return handsFree is not null && StopRecording(handsFree);
    }

    private void StartRecording(bool handsFree)
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

            if (_usage?.IsBudgetExhausted == true)
            {
                _log.Warn("Monthly budget exhausted; dictation blocked.");
                _notifier.ShowError("Monatsbudget erreicht – Diktieren ist gesperrt. Limit in den Einstellungen unter „Kosten“ anpassbar.");
                return;
            }

            if (_passwordFields.IsFocusInPasswordField())
            {
                _log.Info("Focus is in a password field; dictation blocked.");
                _notifier.ShowError("Passwortfeld erkannt – hier ist Diktieren deaktiviert.");
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

            var dictation = new ActiveDictation(
                target,
                session,
                _mode.Current,
                handsFree,
                _notifier,
                new SpeechActivityTracker(
                    _settings.Audio.SpeechLevelThreshold,
                    TimeSpan.FromMilliseconds(_settings.Audio.SegmentPauseMilliseconds),
                    TimeSpan.FromSeconds(_settings.Audio.MinSegmentSeconds),
                    TimeSpan.FromSeconds(_settings.Audio.HandsFreeSilenceTimeoutSeconds)));
            dictation.SilenceTimeoutReached += OnSilenceTimeout;
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
            _log.Info($"Recording started ({dictation.Mode}{(handsFree ? ", hands-free" : string.Empty)}). Target: {target}.");
        }

        _notifier.SetState(DictationState.Recording);
        _notifier.SetHandsFree(handsFree);
    }

    /// <summary>
    /// Safety net above the per-attempt timeouts and retries of the services (FR-036), so a hung
    /// request can never block TK Voice: every attempt plus backoff, plus a margin.
    /// </summary>
    private TimeSpan OverallTimeout(int attemptTimeoutSeconds) =>
        TimeSpan.FromSeconds(attemptTimeoutSeconds * (_settings.Processing.MaxRetries + 1) + 15);

    /// <summary>
    /// The microphone disappeared mid-recording: keep what was said so far and process it, so the
    /// dictation is not lost. Raised on a capture thread, hence the hand-off.
    /// </summary>
    private void OnMicrophoneFailed(object? sender, Exception exception)
    {
        ActiveDictation? dictation;
        lock (_gate)
        {
            dictation = _current;
        }

        _log.Error("Microphone failed during recording.", exception);
        _notifier.ShowError("Mikrofon getrennt – das bisher Gesagte wird verarbeitet.");
        if (dictation is not null)
        {
            _ = Task.Run(() => StopRecording(dictation));
        }
    }

    /// <summary>Raised on the audio thread, which must not wait for itself to stop.</summary>
    private void OnSilenceTimeout(ActiveDictation dictation)
    {
        _log.Info($"Hands-free recording stopped after {_settings.Audio.HandsFreeSilenceTimeoutSeconds} s of silence.");
        _ = Task.Run(() => StopRecording(dictation));
    }

    /// <summary>Stops <paramref name="dictation"/> if it is still the one recording. Returns whether it did.</summary>
    private bool StopRecording(ActiveDictation dictation)
    {
        lock (_gate)
        {
            if (_state != DictationState.Recording || !ReferenceEquals(_current, dictation))
            {
                return false;
            }

            _audio.Stop();
            _sounds.PlayRecordingStopped();
            _current = null;
            _state = DictationState.Processing;
            ProcessingCompletion = Task.Run(() => ProcessAsync(dictation));
        }

        _log.Info($"Recording stopped after {dictation.AudioDuration.TotalSeconds:F1} s of audio.");
        _notifier.SetState(DictationState.Processing);
        return true;
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

            // Streamed audio is billed whether or not transcription succeeds.
            _usage?.RecordTranscription(dictation.AudioDuration);

            using var timeout = new CancellationTokenSource(OverallTimeout(_settings.Processing.TimeoutSeconds));
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

            if (result == InsertionResult.PasswordField)
            {
                _log.Info("Insertion blocked: password field.");
                _notifier.ShowError("Passwortfeld erkannt – der Text wurde nicht eingefügt.");
                return;
            }

            _log.Info($"Text inserted. Stop-to-insert latency: {(DateTimeOffset.UtcNow - stopped).TotalMilliseconds:F0} ms.");
            LearnSpelledTerms(transcript, text);
            _debugLog.Record(dictation.Target.DisplayName, dictation.Mode, transcript, text);
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Processing hit the overall safety timeout.");
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
            using var timeout = new CancellationTokenSource(OverallTimeout(_settings.Processing.SmartTimeoutSeconds));
            var rule = AppRule.Find(_settings.AppRules, dictation.Target.ProcessName);
            var request = new TextProcessingRequest(
                transcript,
                dictation.Target.DisplayName,
                _settings.Processing.SendWindowTitle ? dictation.Target.WindowTitle : null,
                rule?.Style);
            _log.Debug($"App rule: {rule?.Name ?? "none"}.");
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

    /// <summary>FR-021: explicitly spelled terms become dictionary entries.</summary>
    private void LearnSpelledTerms(string transcript, string insertedText)
    {
        if (!_settings.Dictionary.LearnSpelledTerms)
        {
            return;
        }

        foreach (var term in SpelledTermDetector.Detect(transcript, insertedText))
        {
            if (_dictionary.Add(term))
            {
                _log.Info($"Dictionary entry learned from spelling ({term.Length} chars).");
                _notifier.ShowInfo($"„{term}“ ins Wörterbuch übernommen");
            }
        }
    }

    private sealed class ActiveDictation(
        DictationTarget target,
        ITranscriptionSession session,
        ProcessingMode mode,
        bool handsFree,
        IUserNotifier notifier,
        SpeechActivityTracker speechActivity)
    {
        private long _audioBytes;
        private volatile bool _handsFree = handsFree;
        private bool _silenceTimeoutRaised;

        public event Action<ActiveDictation>? SilenceTimeoutReached;

        public DictationTarget Target { get; } = target;
        public ProcessingMode Mode { get; } = mode;

        public bool HandsFree
        {
            get => _handsFree;
            set => _handsFree = value;
        }

        public ITranscriptionSession Session { get; } = session;
        public TimeSpan AudioDuration => AudioFormat.DurationOf(Interlocked.Read(ref _audioBytes));

        public void OnAudio(ReadOnlyMemory<byte> pcm)
        {
            Interlocked.Add(ref _audioBytes, pcm.Length);
            Session.AppendAudio(pcm);

            var level = AudioLevel.Compute(pcm.Span);
            notifier.ReportAudioLevel(level);

            var activity = speechActivity.OnChunk(level, AudioFormat.DurationOf(pcm.Length));
            if (activity.CommitSegment)
            {
                Session.CommitSegment();
            }

            if (activity.SilenceTimeoutReached && HandsFree && !_silenceTimeoutRaised)
            {
                _silenceTimeoutRaised = true;
                SilenceTimeoutReached?.Invoke(this);
            }
        }
    }
}
