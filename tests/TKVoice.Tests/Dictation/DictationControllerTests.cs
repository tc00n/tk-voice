using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictation;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Settings;

namespace TKVoice.Tests.Dictation;

public class DictationControllerTests
{
    private static readonly DictationTarget Notepad = new(0x1234, 42, "notepad");
    private static readonly byte[] OneSecondOfAudio = new byte[AudioFormat.BytesPerSecond];

    private readonly FakeAudio _audio = new();
    private readonly FakeTranscription _transcription = new();
    private readonly FakeTargetCapture _targetCapture = new() { Target = Notepad };
    private readonly FakeInsertion _insertion = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeSounds _sounds = new();
    private readonly TKVoiceSettings _settings = new();

    private DictationController CreateController() =>
        new(_audio, _transcription, _targetCapture, _insertion, _notifier, _sounds, new NullLog(), _settings);

    [Fact]
    public async Task Push_to_talk_records_transcribes_and_inserts_into_captured_target()
    {
        var controller = CreateController();
        _transcription.Result = "Hallo Welt";

        controller.OnHotkeyPressed(HotkeyAction.PushToTalk);
        Assert.Equal(DictationState.Recording, controller.State);
        Assert.True(_audio.IsRunning);

        _audio.Emit(OneSecondOfAudio);
        controller.OnHotkeyReleased(HotkeyAction.PushToTalk);
        Assert.False(_audio.IsRunning);

        await controller.ProcessingCompletion;

        Assert.Equal([(Notepad, "Hallo Welt")], _insertion.Inserted);
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
        _transcription.Result = "Text";

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
        _transcription.Result = "Text";
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

        public void AppendAudio(ReadOnlyMemory<byte> pcm) => AppendedBytes += pcm.Length;

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

        public void ShowError(string message) => Errors.Add(message);
    }
}
