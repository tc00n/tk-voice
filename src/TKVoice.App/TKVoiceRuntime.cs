using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictation;
using TKVoice.Core.Dictionary;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;
using TKVoice.Core.Usage;
using TKVoice.Infrastructure;
using TKVoice.Infrastructure.Audio;
using TKVoice.Infrastructure.Clipboard;
using TKVoice.Infrastructure.Hotkeys;
using TKVoice.Infrastructure.Logging;
using TKVoice.Infrastructure.Security;
using TKVoice.Infrastructure.Targeting;
using TKVoice.Infrastructure.TextInsertion;
using TKVoice.OpenAI;
using TKVoice.OpenAI.Smart;

namespace TKVoice.App;

/// <summary>Services that live as long as the application, independent of settings changes.</summary>
internal sealed record SharedServices(
    ILog Log,
    ICredentialService Credentials,
    PersonalDictionary Dictionary,
    ProcessingModeState Mode,
    Win32ClipboardService Clipboard,
    IUserNotifier Notifier,
    UsageTracker Usage,
    UiaPasswordFieldDetector PasswordFields,
    Func<bool> IsActive);

/// <summary>
/// Everything built from the settings: dictation pipeline and global hotkeys. Saving settings
/// disposes the runtime and builds a new one, so every option applies without restarting TK Voice.
/// </summary>
internal sealed class TKVoiceRuntime : IDisposable
{
    private readonly OpenAISmartTextProcessor _smartProcessor;
    private readonly List<string> _hotkeyProblems = [];

    public TKVoiceRuntime(TKVoiceSettings settings, SharedServices shared)
    {
        var log = shared.Log;
        _smartProcessor = new OpenAISmartTextProcessor(
            settings.OpenAI, settings.Processing, shared.Credentials, () => shared.Dictionary.Terms, log, usage: shared.Usage);
        var addToDictionary = new AddToDictionaryCommand(new ClipboardSelectionReader(shared.Clipboard, log), shared.Dictionary, shared.Notifier, log);

        Controller = new DictationController(
            new WaveInAudioCaptureService(settings.Audio.InputDeviceName, log),
            new RealtimeTranscriptionService(
                settings.OpenAI,
                settings.Processing,
                shared.Credentials,
                () => shared.Dictionary.Terms.TakeLast(settings.Dictionary.MaxTranscriptionKeywords).ToList(),
                log),
            _smartProcessor,
            shared.Mode,
            shared.Dictionary,
            new ForegroundWindowTargetCaptureService(settings.Processing.SendWindowTitle, log),
            new WindowsTextInsertionService(shared.Clipboard, settings.Insertion, shared.PasswordFields, log),
            shared.Notifier,
            new ToneSoundService(settings.Audio.SoundsEnabled, settings.Audio.SoundVolume, log),
            log,
            settings,
            shared.Usage,
            shared.PasswordFields,
            settings.Diagnostics.DebugMode ? new FileDictationDebugLog(AppPaths.DebugDirectory) : new NoDictationDebugLog());

        Hotkeys = new LowLevelHotkeyService(log);
        Hotkeys.Pressed += (_, action) =>
        {
            if (!shared.IsActive())
            {
                return;
            }

            if (action == HotkeyAction.AddToDictionary)
            {
                addToDictionary.Execute();
            }
            else
            {
                Controller.OnHotkeyPressed(action);
            }
        };
        Hotkeys.Released += (_, action) => Controller.OnHotkeyReleased(action);

        PushToTalk = Register(HotkeyAction.PushToTalk, settings.Hotkeys.PushToTalk, new HotkeySettings().PushToTalk, log);
        Register(HotkeyAction.HandsFreeToggle, settings.Hotkeys.HandsFree, fallback: null, log);
        Register(HotkeyAction.ToggleSmartRaw, settings.Hotkeys.ToggleSmartRaw, fallback: null, log);
        Register(HotkeyAction.AddToDictionary, settings.Hotkeys.AddToDictionary, fallback: null, log);
        Hotkeys.Start();
    }

    public DictationController Controller { get; }

    public IHotkeyService Hotkeys { get; }

    public HotkeyGesture? PushToTalk { get; }

    /// <summary>Invalid hotkeys from the settings file, to be shown to the user once.</summary>
    public IReadOnlyList<string> HotkeyProblems => _hotkeyProblems;

    public void Dispose()
    {
        Hotkeys.Dispose();
        _smartProcessor.Dispose();
    }

    /// <summary>Registers a configured hotkey; an invalid one falls back to the default or stays disabled.</summary>
    private HotkeyGesture? Register(HotkeyAction action, string configured, string? fallback, ILog log)
    {
        if (string.IsNullOrWhiteSpace(configured) && fallback is null)
        {
            return null;
        }

        if (!HotkeyGesture.TryParse(configured, out var gesture, out var error))
        {
            log.Warn(error);
            if (fallback is null)
            {
                _hotkeyProblems.Add($"{error} Hotkey deaktiviert.");
                return null;
            }

            _hotkeyProblems.Add($"{error} Standard wird verwendet.");
            gesture = HotkeyGesture.Parse(fallback);
        }

        Hotkeys.Register(action, gesture);
        return gesture;
    }
}
