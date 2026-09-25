using System.Windows;
using TKVoice.App.FlowBar;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictation;
using TKVoice.Core.Dictionary;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;
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

/// <summary>Composition root. TK Voice runs in the background with a tray icon and no main window.</summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TKVoice.SingleInstance";

    private Mutex? _singleInstance;
    private FileLog? _log;
    private TrayIcon? _tray;
    private IHotkeyService? _hotkeys;
    private Win32ClipboardService? _clipboard;
    private OpenAISmartTextProcessor? _smartProcessor;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("TK Voice läuft bereits.", "TK Voice", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var settings = new JsonSettingsStore(AppPaths.SettingsFile).LoadOrCreate();
        _log = new FileLog(AppPaths.LogDirectory, settings.Diagnostics.LogLevel);
        _log.Info($"TK Voice {typeof(App).Assembly.GetName().Version} starting.");
        DispatcherUnhandledException += (_, args) =>
        {
            _log.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
        };

        var credentials = new WindowsCredentialService();
        var mode = new ProcessingModeState(
            Enum.TryParse<ProcessingMode>(settings.Processing.DefaultMode, ignoreCase: true, out var defaultMode) ? defaultMode : ProcessingMode.Smart);
        var dictionary = new PersonalDictionary(AppPaths.DictionaryFile);
        DictationController? controller = null;
        _tray = new TrayIcon(credentials, mode, () => controller?.OnHotkeyPressed(HotkeyAction.ToggleSmartRaw), dictionary, _log);
        var notifier = new CompositeNotifier(_tray, new FlowBarOverlay(new FlowBarWindow()));

        _clipboard = new Win32ClipboardService(SynchronizationContext.Current!, _log);
        _smartProcessor = new OpenAISmartTextProcessor(settings.OpenAI, settings.Processing, credentials, () => dictionary.Terms, _log);
        var addToDictionary = new AddToDictionaryCommand(new ClipboardSelectionReader(_clipboard, _log), dictionary, notifier, _log);

        controller = new DictationController(
            new WaveInAudioCaptureService(settings.Audio.InputDeviceNumber, _log),
            new RealtimeTranscriptionService(
                settings.OpenAI,
                settings.Processing,
                credentials,
                () => dictionary.Terms.TakeLast(settings.Dictionary.MaxTranscriptionKeywords).ToList(),
                _log),
            _smartProcessor,
            mode,
            dictionary,
            new ForegroundWindowTargetCaptureService(settings.Processing.SendWindowTitle, _log),
            new WindowsTextInsertionService(_clipboard, settings.Insertion, _log),
            notifier,
            new ToneSoundService(settings.Audio.SoundsEnabled, settings.Audio.SoundVolume, _log),
            _log,
            settings);

        _hotkeys = new LowLevelHotkeyService(_log);
        _hotkeys.Pressed += (_, action) =>
        {
            if (action == HotkeyAction.AddToDictionary)
            {
                addToDictionary.Execute();
            }
            else
            {
                controller.OnHotkeyPressed(action);
            }
        };
        _hotkeys.Released += (_, action) => controller.OnHotkeyReleased(action);

        var pushToTalk = RegisterHotkey(HotkeyAction.PushToTalk, settings.Hotkeys.PushToTalk, new HotkeySettings().PushToTalk);
        RegisterHotkey(HotkeyAction.HandsFreeToggle, settings.Hotkeys.HandsFree, fallback: null);
        RegisterHotkey(HotkeyAction.ToggleSmartRaw, settings.Hotkeys.ToggleSmartRaw, fallback: null);
        RegisterHotkey(HotkeyAction.AddToDictionary, settings.Hotkeys.AddToDictionary, fallback: null);
        _hotkeys.Start();

        if (string.IsNullOrWhiteSpace(credentials.GetOpenAIApiKey()))
        {
            _tray.PromptForApiKey();
        }
        else
        {
            _tray.ShowInfo($"TK Voice ist aktiv ({mode.Current}). Push-to-talk: {pushToTalk}.");
        }
    }

    /// <summary>Registers a configured hotkey; an invalid one falls back to the default or stays disabled.</summary>
    private HotkeyGesture? RegisterHotkey(HotkeyAction action, string configured, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(configured) && fallback is null)
        {
            return null;
        }

        if (!HotkeyGesture.TryParse(configured, out var gesture, out var error))
        {
            _log!.Warn(error);
            if (fallback is null)
            {
                _tray!.ShowError($"{error} Hotkey deaktiviert.");
                return null;
            }

            _tray!.ShowError($"{error} Standard wird verwendet.");
            gesture = HotkeyGesture.Parse(fallback);
        }

        _hotkeys!.Register(action, gesture);
        return gesture;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _clipboard?.Dispose();
        _smartProcessor?.Dispose();
        _tray?.Dispose();
        _log?.Info("TK Voice stopped.");
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
