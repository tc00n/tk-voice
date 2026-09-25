using System.Windows;
using TKVoice.App.FlowBar;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictation;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Settings;
using TKVoice.Infrastructure;
using TKVoice.Infrastructure.Audio;
using TKVoice.Infrastructure.Hotkeys;
using TKVoice.Infrastructure.Logging;
using TKVoice.Infrastructure.Security;
using TKVoice.Infrastructure.Targeting;
using TKVoice.Infrastructure.TextInsertion;
using TKVoice.OpenAI;

namespace TKVoice.App;

/// <summary>Composition root. TK Voice runs in the background with a tray icon and no main window.</summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TKVoice.SingleInstance";

    private Mutex? _singleInstance;
    private FileLog? _log;
    private TrayIcon? _tray;
    private IHotkeyService? _hotkeys;

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
        _tray = new TrayIcon(credentials, _log);
        var notifier = new CompositeNotifier(_tray, new FlowBarOverlay(new FlowBarWindow()));

        var controller = new DictationController(
            new WaveInAudioCaptureService(settings.Audio.InputDeviceNumber, _log),
            new RealtimeTranscriptionService(settings.OpenAI, credentials, _log),
            new ForegroundWindowTargetCaptureService(_log),
            new SendInputTextInsertionService(_log),
            notifier,
            _log,
            settings);

        _hotkeys = new LowLevelHotkeyService(_log);
        _hotkeys.Pressed += (_, action) => controller.OnHotkeyPressed(action);
        _hotkeys.Released += (_, action) => controller.OnHotkeyReleased(action);

        if (!HotkeyGesture.TryParse(settings.Hotkeys.PushToTalk, out var pushToTalk, out var error))
        {
            _log.Warn(error);
            _tray.ShowError($"{error} Standard wird verwendet.");
            pushToTalk = HotkeyGesture.Parse(new HotkeySettings().PushToTalk);
        }

        _hotkeys.Register(HotkeyAction.PushToTalk, pushToTalk);
        _hotkeys.Start();

        if (string.IsNullOrWhiteSpace(credentials.GetOpenAIApiKey()))
        {
            _tray.PromptForApiKey();
        }
        else
        {
            _tray.ShowInfo($"TK Voice ist aktiv. Push-to-talk: {pushToTalk}.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _log?.Info("TK Voice stopped.");
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
