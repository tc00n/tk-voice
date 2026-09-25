using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictation;
using TKVoice.Core.Dictionary;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;
using TKVoice.Core.Usage;

namespace TKVoice.Tests.Dictation;

public class DictationControllerTests : IDisposable
{
    private static readonly DictationTarget Notepad = new(0x1234, 42, "notepad");
    private static readonly byte[] OneSecondOfAudio = new byte[AudioFormat.BytesPerSecond];

    private readonly FakeAudio _audio = new();
    private readonly FakeTranscription _transcription = new();
    private readonly FakeTargetCapture _targetCapture = new() { Target = Notepad };
    private readonly FakeInsertion _insertion = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeSounds _sounds = new();
    private readonly FakeSmartProcessor _smart = new();
    private readonly ProcessingModeState _mode = new(ProcessingMode.Smart);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tkvoice-tests-" + Guid.NewGuid().ToString("N"));
    private PersonalDictionary? _dictionary;
    private readonly FakePasswordFields _passwordFields = new();
    private readonly FakeDebugLog _debugLog = new();
    private UsageTracker? _usage;

    private UsageTracker Usage => _usage ??= new UsageTracker(Path.Combine(_directory, "usage.json"), () => _settings.Costs);

    private PersonalDictionary Dictionary => _dictionary ??= new PersonalDictionary(Path.Combine(_directory, "dictionary.json"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
    private readonly TKVoiceSettings _settings = new();

    private DictationController CreateController() =>
        new(_audio, _transcription, _smart, _mode, Dictionary, _targetCapture, _insertion, _notifier, _sounds, new NullLog(), _settings, Usage, _passwordFields, _debugLog);

    private async Task DictateAsync(DictationController controller)
    {
        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        await controller.ProcessingCompletion;
    }

    [Fact]
    public async Task Smart_mode_inserts_processed_text_with_app_context()
    {
        var controller = CreateController();
        _transcription.Result = "Ähm wir treffen uns Dienstag, nein Mittwoch.";
        _smart.Output = "Wir treffen uns Mittwoch.";

        await DictateAsync(controller);

        Assert.Equal("Wir treffen uns Mittwoch.", _insertion.Inserted.Single().Text);
        Assert.Equal("notepad", _smart.Requests.Single().ApplicationName);
        Assert.Null(_smart.Requests.Single().WindowTitle);
    }

    [Fact]
    public async Task App_rule_style_and_display_name_are_passed_to_smart_processing()
    {
        _targetCapture.Target = new DictationTarget(0x42, 1, "OUTLOOK") { ApplicationDescription = "Microsoft Outlook" };
        var controller = CreateController();
        _transcription.Result = "Ähm, wir sehen uns morgen.";
        _smart.Output = "Wir sehen uns morgen.";

        await DictateAsync(controller);

        var request = _smart.Requests.Single();
        Assert.Equal("Microsoft Outlook (OUTLOOK)", request.ApplicationName);
        Assert.StartsWith("E-mail.", request.AppStyle);
    }

    [Fact]
    public async Task Applications_without_rule_get_no_style()
    {
        var controller = CreateController();
        _transcription.Result = "Ähm, wir sehen uns morgen.";
        _smart.Output = "Wir sehen uns morgen.";

        await DictateAsync(controller);

        Assert.Null(_smart.Requests.Single().AppStyle);
    }

    [Fact]
    public async Task Raw_mode_inserts_transcript_without_smart_processing()
    {
        var controller = CreateController();
        _mode.Toggle();
        _transcription.Result = "Ähm also wir müssen quasi morgen äh mit Peter sprechen.";

        await DictateAsync(controller);

        Assert.Equal(_transcription.Result, _insertion.Inserted.Single().Text);
        Assert.Empty(_smart.Requests);
    }

    [Fact]
    public async Task Mode_is_fixed_at_recording_start()
    {
        var controller = CreateController();
        _transcription.Result = "Ähm, Test.";
        _smart.Output = "Test.";

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        controller.OnHotkeyPressed(HotkeyAction.ToggleSmartRaw);
        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        await controller.ProcessingCompletion;

        Assert.Equal("Test.", _insertion.Inserted.Single().Text);
        Assert.Equal(ProcessingMode.Raw, _mode.Current);
    }

    [Fact]
    public async Task Simple_text_skips_smart_processing()
    {
        var controller = CreateController();
        _transcription.Result = "Ich bin gleich da.";

        await DictateAsync(controller);

        Assert.Equal("Ich bin gleich da.", _insertion.Inserted.Single().Text);
        Assert.Empty(_smart.Requests);
    }

    [Fact]
    public async Task Smart_failure_falls_back_to_raw_transcript_and_informs_user()
    {
        var controller = CreateController();
        _transcription.Result = "Ähm, das ist ein Test.";
        _smart.Error = new SmartProcessingException("offline");

        await DictateAsync(controller);

        Assert.Equal("Ähm, das ist ein Test.", _insertion.Inserted.Single().Text);
        Assert.Single(_notifier.Errors);
    }

    [Fact]
    public async Task Smart_output_that_answers_instead_of_cleaning_is_rejected()
    {
        var controller = CreateController();
        _transcription.Result = "Äh, wie spät ist es?";
        _smart.Output = "Es ist aktuell 14:32 Uhr. Wenn du möchtest, kann ich dir auch die Zeit in anderen Zeitzonen nennen oder einen Wecker stellen.";

        await DictateAsync(controller);

        Assert.Equal("Äh, wie spät ist es?", _insertion.Inserted.Single().Text);
    }

    [Fact]
    public async Task Empty_smart_output_inserts_nothing()
    {
        var controller = CreateController();
        _transcription.Result = "Ähm, äh.";
        _smart.Output = "";

        await DictateAsync(controller);

        Assert.Empty(_insertion.Inserted);
        Assert.Empty(_notifier.Errors);
    }

    [Fact]
    public async Task Spelled_term_is_learned_into_dictionary()
    {
        var controller = CreateController();
        _transcription.Result = "Das Projekt heißt NEONEX, geschrieben N-E-O-N-E-X.";
        _smart.Output = "Das Projekt heißt NEONEX.";

        await DictateAsync(controller);

        Assert.Equal(["NEONEX"], Dictionary.Terms);
        Assert.Contains("NEONEX", _notifier.Infos.Single());
    }

    [Fact]
    public async Task Learning_can_be_disabled()
    {
        _settings.Dictionary.LearnSpelledTerms = false;
        var controller = CreateController();
        _transcription.Result = "Das Projekt heißt NEONEX, geschrieben N-E-O-N-E-X.";
        _smart.Output = "Das Projekt heißt NEONEX.";

        await DictateAsync(controller);

        Assert.Empty(Dictionary.Terms);
    }

    [Fact]
    public async Task Hands_free_hotkey_starts_and_stops_a_recording()
    {
        var controller = CreateController();
        _transcription.Result = "Freihändig diktiert.";

        controller.OnHotkeyPressed(HotkeyAction.HandsFreeToggle);
        Assert.Equal(DictationState.Recording, controller.State);
        Assert.Equal([true], _notifier.HandsFree);

        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyPressed(HotkeyAction.HandsFreeToggle);
        await controller.ProcessingCompletion;

        Assert.Equal("Freihändig diktiert.", _insertion.Inserted.Single().Text);
    }

    [Fact]
    public async Task Push_to_talk_can_be_locked_into_hands_free_and_stopped_by_a_tap()
    {
        var controller = CreateController();
        _transcription.Result = "Lange Aufnahme.";

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        controller.OnHotkeyPressed(HotkeyAction.HandsFreeToggle); // RightCtrl+Space
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        _audio.Emit(OneSecondOfAudio);
        Assert.Equal(DictationState.Recording, controller.State);
        Assert.Equal([false, true], _notifier.HandsFree);

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk); // tap to stop
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        await controller.ProcessingCompletion;

        Assert.Equal("Lange Aufnahme.", _insertion.Inserted.Single().Text);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Silence_timeout_stops_hands_free_recording()
    {
        _settings.Audio.HandsFreeSilenceTimeoutSeconds = 2;
        var controller = CreateController();
        _transcription.Result = "Text.";

        controller.OnHotkeyPressed(HotkeyAction.HandsFreeToggle);
        _audio.Emit(Tone(TimeSpan.FromSeconds(1)));
        for (var i = 0; i < 25; i++)
        {
            _audio.Emit(new byte[AudioFormat.BytesPerSecond / 10]); // 2.5 s of silence
        }

        await WaitUntilAsync(() => controller.State != DictationState.Recording);
        await controller.ProcessingCompletion;
        Assert.Equal("Text.", _insertion.Inserted.Single().Text);
    }

    [Fact]
    public void Silence_timeout_does_not_stop_push_to_talk()
    {
        _settings.Audio.HandsFreeSilenceTimeoutSeconds = 1;
        var controller = CreateController();

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        for (var i = 0; i < 30; i++)
        {
            _audio.Emit(new byte[AudioFormat.BytesPerSecond / 10]);
        }

        Assert.Equal(DictationState.Recording, controller.State);
    }

    [Fact]
    public void Speech_pause_commits_a_transcription_segment()
    {
        var controller = CreateController();

        controller.OnHotkeyPressed(HotkeyAction.HandsFreeToggle);
        _audio.Emit(Tone(TimeSpan.FromSeconds(11)));
        _audio.Emit(new byte[AudioFormat.BytesPerSecond]);

        Assert.Equal(1, _transcription.Session!.SegmentCommits);
    }

    private static byte[] Tone(TimeSpan duration)
    {
        var samples = (int)(duration.TotalSeconds * AudioFormat.SampleRate);
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), (short)(i % 2 == 0 ? 8000 : -8000));
        }

        return bytes;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task Microphone_failure_processes_what_was_said_so_far()
    {
        var controller = CreateController();
        _transcription.Result = "Bis hierhin.";

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        _audio.Emit(OneSecondOfAudio);
        _audio.Fail();

        await WaitUntilAsync(() => controller.State != DictationState.Recording);
        await controller.ProcessingCompletion;
        Assert.Equal("Bis hierhin.", _insertion.Inserted.Single().Text);
        Assert.Contains(_notifier.Errors, e => e.Contains("Mikrofon"));
    }

    [Fact]
    public void Password_field_blocks_dictation_before_the_microphone_opens()
    {
        _passwordFields.IsPassword = true;
        var controller = CreateController();

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);

        Assert.False(_audio.IsRunning);
        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Contains("Passwortfeld", _notifier.Errors.Single());
    }

    [Fact]
    public async Task Password_field_at_insertion_time_is_reported()
    {
        var controller = CreateController();
        _transcription.Result = "Geheim.";
        _insertion.Result = InsertionResult.PasswordField;

        await DictateAsync(controller);

        Assert.Contains("Passwortfeld", _notifier.Errors.Single());
    }

    [Fact]
    public void Exhausted_budget_blocks_dictation()
    {
        _settings.Costs.MonthlyLimitUsd = 0.01;
        Usage.RecordTranscription(TimeSpan.FromMinutes(10));
        var controller = CreateController();

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);

        Assert.False(_audio.IsRunning);
        Assert.Contains(_notifier.Errors, e => e.Contains("Monatsbudget"));
    }

    [Fact]
    public async Task Transcribed_audio_is_recorded_as_usage()
    {
        var controller = CreateController();
        _transcription.Result = "Text.";

        await DictateAsync(controller);

        Assert.Equal(1, Usage.CurrentMonth.TranscriptionRequests);
        Assert.Equal(1.0, Usage.CurrentMonth.TranscriptionSeconds, 3);
    }

    [Fact]
    public async Task Debug_log_receives_transcript_and_inserted_text()
    {
        var controller = CreateController();
        _transcription.Result = "Ähm, Hallo.";
        _smart.Output = "Hallo.";

        await DictateAsync(controller);

        Assert.Equal(("notepad", ProcessingMode.Smart, "Ähm, Hallo.", "Hallo."), _debugLog.Entries.Single());
    }

    [Fact]
    public void Toggle_hotkey_switches_mode_and_informs_user()
    {
        var controller = CreateController();

        controller.OnHotkeyPressed(HotkeyAction.ToggleSmartRaw);

        Assert.Equal(ProcessingMode.Raw, _mode.Current);
        Assert.Equal([ProcessingMode.Raw], _notifier.Modes);
    }

    [Fact]
    public async Task Push_to_talk_records_transcribes_and_inserts_into_captured_target()
    {
        var controller = CreateController();
        _transcription.Result = "Hallo Welt.";

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        Assert.Equal(DictationState.Recording, controller.State);
        Assert.True(_audio.IsRunning);

        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        Assert.False(_audio.IsRunning);

        await controller.ProcessingCompletion;

        Assert.Equal([(Notepad, "Hallo Welt.")], _insertion.Inserted);
        Assert.Equal(OneSecondOfAudio.Length, _transcription.Session!.AppendedBytes);
        Assert.True(_transcription.Session.Disposed);
        Assert.Equal([DictationState.Recording, DictationState.Processing, DictationState.Idle], _notifier.States);
        Assert.Equal(["started", "stopped"], _sounds.Played);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Target_stays_the_one_captured_at_start()
    {
        var controller = CreateController();
        _transcription.Result = "Text.";

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        _targetCapture.Target = new DictationTarget(0x9999, 7, "chrome");
        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        await controller.ProcessingCompletion;

        Assert.Equal(Notepad, _insertion.Inserted.Single().Target);
    }

    [Fact]
    public async Task Accidental_tap_is_discarded_without_insertion()
    {
        var controller = CreateController();
        _transcription.Result = "should not appear";

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        _audio.Emit(new byte[AudioFormat.BytesPerSecond / 10]);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        await controller.ProcessingCompletion;

        Assert.Empty(_insertion.Inserted);
        Assert.True(_transcription.Session!.Disposed);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public async Task Unavailable_target_informs_user()
    {
        var controller = CreateController();
        _transcription.Result = "Text.";
        _insertion.Result = InsertionResult.TargetUnavailable;

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        await controller.ProcessingCompletion;

        Assert.Single(_notifier.Errors);
    }

    [Fact]
    public async Task Transcription_failure_informs_user_and_returns_to_idle()
    {
        var controller = CreateController();
        _transcription.Error = new TranscriptionException("offline");

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        await controller.ProcessingCompletion;

        Assert.Empty(_insertion.Inserted);
        Assert.Single(_notifier.Errors);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    [Fact]
    public void No_target_means_no_recording()
    {
        var controller = CreateController();
        _targetCapture.Target = null;

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.False(_audio.IsRunning);
    }

    [Fact]
    public void Missing_api_key_informs_user_without_opening_microphone()
    {
        var controller = CreateController();
        _transcription.StartError = new TranscriptionException("Kein OpenAI API Key hinterlegt.");

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);

        Assert.False(_audio.IsRunning);
        Assert.Single(_notifier.Errors);
        Assert.Empty(_sounds.Played);
        Assert.Equal(DictationState.Idle, controller.State);
    }

    private sealed class FakeAudio : IAudioCaptureService
    {
        private Action<ReadOnlyMemory<byte>>? _onChunk;

        public bool IsRunning => _onChunk is not null;

        public event EventHandler<Exception>? Failed;

        public void Fail() => Failed?.Invoke(this, new InvalidOperationException("unplugged"));

        public void Start(Action<ReadOnlyMemory<byte>> onChunk) => _onChunk = onChunk;

        public void Stop() => _onChunk = null;

        public void Emit(byte[] pcm) => _onChunk!(pcm);
    }

    private sealed class FakeTranscription : ITranscriptionService
    {
        public string Result { get; set; } = string.Empty;
        public Exception? Error { get; set; }
        public Exception? StartError { get; set; }
        public FakeSession? Session { get; private set; }

        public ITranscriptionSession StartSession()
        {
            if (StartError is not null)
            {
                throw StartError;
            }

            return Session = new FakeSession(this);
        }
    }

    private sealed class FakeSession(FakeTranscription owner) : ITranscriptionSession
    {
        public long AppendedBytes { get; private set; }
        public bool Disposed { get; private set; }

        public int SegmentCommits { get; private set; }

        public void AppendAudio(ReadOnlyMemory<byte> pcm) => AppendedBytes += pcm.Length;

        public void CommitSegment() => SegmentCommits++;

        public Task<string> CompleteAsync(CancellationToken cancellationToken) =>
            owner.Error is null ? Task.FromResult(owner.Result) : Task.FromException<string>(owner.Error);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTargetCapture : ITargetCaptureService
    {
        public DictationTarget? Target { get; set; }

        public DictationTarget? CaptureCurrentTarget() => Target;
    }

    private sealed class FakeInsertion : ITextInsertionService
    {
        public InsertionResult Result { get; set; } = InsertionResult.Inserted;
        public List<(DictationTarget Target, string Text)> Inserted { get; } = [];

        public Task<InsertionResult> InsertAsync(DictationTarget target, string text, CancellationToken cancellationToken)
        {
            if (Result == InsertionResult.Inserted)
            {
                Inserted.Add((target, text));
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSmartProcessor : ISmartTextProcessor
    {
        public string Output { get; set; } = string.Empty;
        public Exception? Error { get; set; }
        public List<TextProcessingRequest> Requests { get; } = [];

        public Task<string> ProcessAsync(TextProcessingRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Error is null ? Task.FromResult(Output) : Task.FromException<string>(Error);
        }
    }

    private sealed class FakePasswordFields : IPasswordFieldDetector
    {
        public bool IsPassword { get; set; }

        public bool IsFocusInPasswordField() => IsPassword;
    }

    private sealed class FakeDebugLog : IDictationDebugLog
    {
        public List<(string, ProcessingMode, string, string)> Entries { get; } = [];

        public void Record(string application, ProcessingMode mode, string transcript, string insertedText) =>
            Entries.Add((application, mode, transcript, insertedText));
    }

    private sealed class FakeSounds : ISoundService
    {
        public List<string> Played { get; } = [];

        public void PlayRecordingStarted() => Played.Add("started");

        public void PlayRecordingStopped() => Played.Add("stopped");
    }

    private sealed class FakeNotifier : IUserNotifier
    {
        public List<DictationState> States { get; } = [];
        public List<string> Errors { get; } = [];

        public void SetState(DictationState state)
        {
            lock (States)
            {
                States.Add(state);
            }
        }

        public List<ProcessingMode> Modes { get; } = [];
        public List<string> Infos { get; } = [];
        public List<bool> HandsFree { get; } = [];

        public void SetHandsFree(bool handsFree) => HandsFree.Add(handsFree);

        public void ShowInfo(string message) => Infos.Add(message);

        public void ShowError(string message) => Errors.Add(message);

        public void ShowModeChanged(ProcessingMode mode) => Modes.Add(mode);
    }
}
