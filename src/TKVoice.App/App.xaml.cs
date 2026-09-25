using System.Windows;
using System.Windows.Threading;
using TKVoice.App.FlowBar;
using TKVoice.App.Settings;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Dictionary;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;
using TKVoice.Core.Usage;
using TKVoice.Infrastructure;
using TKVoice.Infrastructure.Clipboard;
using TKVoice.Infrastructure.Logging;
using TKVoice.Infrastructure.Security;

namespace TKVoice.App;

/// <summary>Composition root. TK Voice runs in the background with a tray icon and no main window (FR-042).</summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\TKVoice.SingleInstance";

    private Mutex? _singleInstance;
    private JsonSettingsStore? _store;
    private TKVoiceSettings _settings = new();
    private FileLog? _log;
    private TrayIcon? _tray;
    private FlowBarWindow? _flowBar;
    private Win32ClipboardService? _clipboard;
    private SharedServices? _shared;
    private TKVoiceRuntime? _runtime;
    private SettingsWindow? _settingsWindow;

    internal static new App Current => (App)Application.Current;

    internal TKVoiceSettings Settings => _settings;

    internal SharedServices Shared => _shared!;

    internal FileLog Log => _log!;

    internal bool IsActive => _settings.General.Active;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (InstallerCommands.TryRun(e.Args))
        {
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("TK Voice läuft bereits.", "TK Voice", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        InstallerCommands.ListenForQuit(() => Dispatcher.BeginInvoke(Shutdown));

        _store = new JsonSettingsStore(AppPaths.SettingsFile);
        _settings = _store.LoadOrCreate();
        _log = new FileLog(AppPaths.LogDirectory, _settings.Diagnostics.LogLevel);
        _log.Info($"TK Voice {typeof(App).Assembly.GetName().Version} starting.");
        DispatcherUnhandledException += (_, args) =>
        {
            _log.Error("Unhandled UI exception.", args.Exception);
            args.Handled = true;
        };

        var credentials = new WindowsCredentialService();
        var dictionary = new PersonalDictionary(AppPaths.DictionaryFile);
        var mode = new ProcessingModeState(ParseMode(_settings.Processing.DefaultMode));
        _tray = new TrayIcon(mode, _log);
        _tray.SetActive(IsActive);
        _flowBar = new FlowBarWindow();
        var notifier = new CompositeNotifier(_tray, new FlowBarOverlay(_flowBar));
        _clipboard = new Win32ClipboardService(SynchronizationContext.Current!, _log);
        var usage = new UsageTracker(AppPaths.UsageFile, () => _settings.Costs);
        usage.BudgetWarning += (_, message) =>
        {
            _log.Warn("Budget threshold reached.");
            notifier.ShowError(message);
        };
        var passwordFields = new UiaPasswordFieldDetector(_log);
        passwordFields.Warmup();
        _shared = new SharedServices(_log, credentials, dictionary, mode, _clipboard, notifier, usage, passwordFields, () => IsActive);

        StartRuntime();
        SyncAutostart();

        if (string.IsNullOrWhiteSpace(credentials.GetOpenAIApiKey()))
        {
            OpenSettings(SettingsPage.OpenAI);
        }
        else if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            OpenSettings();
        }
        else
        {
            _tray.ShowInfo(IsActive
                ? $"TK Voice ist aktiv ({mode.Current}). Push-to-talk: {_runtime!.PushToTalk}."
                : "TK Voice ist pausiert.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _runtime?.Dispose();
        _clipboard?.Dispose();
        _tray?.Dispose();
        _log?.Info("TK Voice stopped.");
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    internal void ToggleMode() => _runtime?.Controller.OnHotkeyPressed(HotkeyAction.ToggleSmartRaw);

    internal void SetActive(bool active)
    {
        _settings.General.Active = active;
        _store!.Save(_settings);
        _tray!.SetActive(active);
        _log!.Info(active ? "TK Voice activated." : "TK Voice paused.");
    }

    internal void OpenSettings(SettingsPage page = SettingsPage.General)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(JsonSettingsStore.Clone(_settings), page);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                SuspendHotkeys(false);
            };
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.ShowPage(page);
        }

        _settingsWindow.Activate();
    }

    /// <summary>While the settings window records a hotkey, the live hotkeys must stay quiet.</summary>
    internal void SuspendHotkeys(bool suspended)
    {
        if (_runtime is not null)
        {
            _runtime.Hotkeys.Suspended = suspended;
        }
    }

    /// <summary>Saves and applies new settings. Waits for a running dictation to finish first.</summary>
    internal void ApplySettings(TKVoiceSettings updated)
    {
        _settings = updated;
        _store!.Save(_settings);
        _log!.SetMinimumLevel(_settings.Diagnostics.LogLevel);
        _tray!.SetActive(IsActive);
        SyncAutostart();
        _log.Info("Settings saved.");

        if (_runtime?.Controller.State is DictationState.Recording or DictationState.Processing)
        {
            var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            wait.Tick += (_, _) =>
            {
                if (_runtime?.Controller.State == DictationState.Idle)
                {
                    wait.Stop();
                    RestartRuntime();
                }
            };
            wait.Start();
        }
        else
        {
            RestartRuntime();
        }
    }

    private void RestartRuntime()
    {
        _runtime?.Dispose();
        _runtime = null;
        StartRuntime();
    }

    private void StartRuntime()
    {
        _runtime = new TKVoiceRuntime(_settings, _shared!);
        var debug = _settings.Diagnostics.DebugMode;
        _tray!.SetDebugMode(debug);
        _flowBar!.SetDebugMode(debug);
        if (debug)
        {
            _log!.Warn("Debug mode is ON: dictated content is recorded.");
        }

        foreach (var problem in _runtime.HotkeyProblems)
        {
            _tray!.ShowError(problem);
        }
    }

    private void SyncAutostart()
    {
        try
        {
            AutostartService.Apply(_settings.General.Autostart);
        }
        catch (Exception ex)
        {
            _log!.Error("Autostart could not be configured.", ex);
        }
    }

    private static ProcessingMode ParseMode(string value) =>
        Enum.TryParse<ProcessingMode>(value, ignoreCase: true, out var mode) ? mode : ProcessingMode.Smart;
}
