using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TKVoice.Core.Hotkeys;
using TKVoice.Core.Processing;
using TKVoice.Core.Settings;
using TKVoice.Infrastructure;
using TKVoice.Infrastructure.Audio;
using TKVoice.Infrastructure.Hotkeys;
using TKVoice.Infrastructure.Targeting;
using TKVoice.OpenAI;

namespace TKVoice.App.Settings;

public enum SettingsPage
{
    General,
    Hotkeys,
    Audio,
    Processing,
    Dictionary,
    AppRules,
    OpenAI,
    Diagnostics,
}

/// <summary>
/// "TK Voice – Settings" (§36). Edits a copy of the settings; Speichern validates, stores and applies
/// them without restarting. The dictionary page edits the live dictionary directly.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly TKVoiceSettings _settings;
    private readonly List<AppRule> _rules;
    private bool _loadingRule;

    internal SettingsWindow(TKVoiceSettings settings, SettingsPage page)
    {
        InitializeComponent();
        _settings = settings;
        _rules = settings.AppRules;

        LoadGeneral();
        LoadHotkeys();
        LoadAudio();
        LoadProcessing();
        DictionaryEditor.Bind(App.Current.Shared.Dictionary);
        LoadRules();
        LoadOpenAI();
        LoadDiagnostics();
        ShowPage(page);
    }

    internal void ShowPage(SettingsPage page) =>
        Tabs.SelectedItem = Tabs.Items.Cast<TabItem>().First(tab => (string)tab.Tag == page.ToString());

    // General

    private void LoadGeneral()
    {
        ActiveBox.IsChecked = _settings.General.Active;
        AutostartBox.IsChecked = _settings.General.Autostart;
    }

    // Hotkeys

    private IEnumerable<(string Name, HotkeyBox Box)> HotkeyBoxes =>
    [
        ("Push-to-talk", PushToTalkBox),
        ("Hands-free", HandsFreeBox),
        ("Smart/Raw", ToggleModeBox),
        ("Wörterbuch", AddToDictionaryBox),
    ];

    private void LoadHotkeys()
    {
        PushToTalkBox.Gesture = _settings.Hotkeys.PushToTalk;
        HandsFreeBox.Gesture = _settings.Hotkeys.HandsFree;
        ToggleModeBox.Gesture = _settings.Hotkeys.ToggleSmartRaw;
        AddToDictionaryBox.Gesture = _settings.Hotkeys.AddToDictionary;
        foreach (var (_, box) in HotkeyBoxes)
        {
            box.GestureChanged += (_, _) => UpdateHotkeyWarnings();
        }

        UpdateHotkeyWarnings();
    }

    private void OnResetHotkey(object sender, RoutedEventArgs e)
    {
        var defaults = new HotkeySettings();
        switch ((string)((Button)sender).Tag)
        {
            case "PushToTalk":
                PushToTalkBox.Gesture = defaults.PushToTalk;
                break;
            case "HandsFree":
                HandsFreeBox.Gesture = defaults.HandsFree;
                break;
            case "ToggleSmartRaw":
                ToggleModeBox.Gesture = defaults.ToggleSmartRaw;
                break;
            case "AddToDictionary":
                AddToDictionaryBox.Gesture = defaults.AddToDictionary;
                break;
        }

        UpdateHotkeyWarnings();
    }

    /// <summary>Duplicates are errors; combinations registered by other applications are warnings (FR-001).</summary>
    private List<string> UpdateHotkeyWarnings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var parsed = new List<(string Name, HotkeyGesture Gesture)>();
        foreach (var (name, box) in HotkeyBoxes)
        {
            if (string.IsNullOrWhiteSpace(box.Gesture))
            {
                continue;
            }

            if (!HotkeyGesture.TryParse(box.Gesture, out var gesture, out var error))
            {
                errors.Add($"{name}: {error}");
                continue;
            }

            if (parsed.FirstOrDefault(p => p.Gesture.IsEquivalentTo(gesture)) is { Name: not null } duplicate)
            {
                errors.Add($"{name} und {duplicate.Name} haben dieselbe Tastenkombination.");
            }

            if (HotkeyConflictProbe.IsTakenByAnotherApplication(gesture))
            {
                warnings.Add($"{name} ({gesture}) ist bereits von einem anderen Programm als globaler Hotkey belegt.");
            }

            parsed.Add((name, gesture));
        }

        if (string.IsNullOrWhiteSpace(PushToTalkBox.Gesture))
        {
            errors.Add("Push-to-talk braucht einen Hotkey.");
        }

        HotkeyWarnings.Text = string.Join(Environment.NewLine, errors.Concat(warnings));
        return errors;
    }

    // Audio

    private void LoadAudio()
    {
        MicrophoneBox.Items.Add(new ComboBoxItem { Content = "Windows-Standardmikrofon", Tag = string.Empty });
        foreach (var name in MicrophoneCatalog.ListDeviceNames())
        {
            MicrophoneBox.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }

        var configured = _settings.Audio.InputDeviceName;
        var match = MicrophoneBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, configured, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            match = new ComboBoxItem { Content = $"{configured} (nicht verbunden)", Tag = configured };
            MicrophoneBox.Items.Add(match);
        }

        MicrophoneBox.SelectedItem = match;
        SoundsBox.IsChecked = _settings.Audio.SoundsEnabled;
        VolumeSlider.Value = _settings.Audio.SoundVolume;
        SilenceTimeoutBox.Text = _settings.Audio.HandsFreeSilenceTimeoutSeconds.ToString();
    }

    private void OnTestSound(object sender, RoutedEventArgs e) =>
        new ToneSoundService(enabled: true, VolumeSlider.Value, App.Current.Log).PlayRecordingStarted();

    // Processing

    private void LoadProcessing()
    {
        DefaultModeBox.SelectedItem = DefaultModeBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals((string)i.Tag, _settings.Processing.DefaultMode, StringComparison.OrdinalIgnoreCase))
            ?? DefaultModeBox.Items[0];
        TimeoutBox.Text = _settings.Processing.TimeoutSeconds.ToString();
        SmartTimeoutBox.Text = _settings.Processing.SmartTimeoutSeconds.ToString();
        RetriesBox.Text = _settings.Processing.MaxRetries.ToString();
        SkipSimpleBox.IsChecked = _settings.Processing.SkipSmartForSimpleText;
        WindowTitleBox.IsChecked = _settings.Processing.SendWindowTitle;
    }

    // App rules

    private void LoadRules()
    {
        RuleList.ItemsSource = _rules;
        RuleList.SelectedIndex = _rules.Count > 0 ? 0 : -1;
        UpdateRuleEditor();
    }

    private AppRule? SelectedRule => RuleList.SelectedItem as AppRule;

    private void OnRuleSelected(object sender, SelectionChangedEventArgs e) => UpdateRuleEditor();

    private void UpdateRuleEditor()
    {
        _loadingRule = true;
        var rule = SelectedRule;
        RuleEditor.IsEnabled = rule is not null;
        DeleteRuleButton.IsEnabled = rule is not null;
        RuleNameBox.Text = rule?.Name ?? string.Empty;
        RuleProcessesBox.Text = rule is null ? string.Empty : string.Join(", ", rule.Processes);
        RuleStyleBox.Text = rule?.Style ?? string.Empty;
        _loadingRule = false;
    }

    private void OnRuleEdited(object sender, TextChangedEventArgs e)
    {
        if (_loadingRule || SelectedRule is not { } rule)
        {
            return;
        }

        rule.Name = RuleNameBox.Text.Trim();
        rule.Processes = SplitList(RuleProcessesBox.Text).Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p).ToList();
        rule.Style = RuleStyleBox.Text.Trim();
        if (sender == RuleNameBox)
        {
            RuleList.Items.Refresh();
        }
    }

    private void OnAddRule(object sender, RoutedEventArgs e)
    {
        var rule = new AppRule { Name = "Neue Regel" };
        _rules.Add(rule);
        RuleList.Items.Refresh();
        RuleList.SelectedItem = rule;
        RuleNameBox.Focus();
        RuleNameBox.SelectAll();
    }

    private void OnDeleteRule(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is { } rule)
        {
            _rules.Remove(rule);
            RuleList.Items.Refresh();
            RuleList.SelectedIndex = _rules.Count > 0 ? 0 : -1;
        }
    }

    private void OnResetRules(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Alle App-Regeln durch die Standardregeln ersetzen?", "TK Voice", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _rules.Clear();
        _rules.AddRange(AppRule.Defaults());
        RuleList.Items.Refresh();
        RuleList.SelectedIndex = 0;
    }

    /// <summary>"Anwendung erkennen": 3 seconds to switch to the application, then its process name is added.</summary>
    private void OnDetectApp(object sender, RoutedEventArgs e)
    {
        var remaining = 3;
        DetectAppButton.IsEnabled = false;
        DetectAppButton.Content = $"Wechseln … {remaining}";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining > 0)
            {
                DetectAppButton.Content = $"Wechseln … {remaining}";
                return;
            }

            timer.Stop();
            DetectAppButton.IsEnabled = true;
            DetectAppButton.Content = "Anwendung erkennen";
            var target = new ForegroundWindowTargetCaptureService(captureWindowTitle: false, App.Current.Log).CaptureCurrentTarget();
            Activate();
            if (target is null)
            {
                MessageBox.Show(this, "Es wurde kein anderes Programm im Vordergrund erkannt.", "TK Voice");
                return;
            }

            var processes = SplitList(RuleProcessesBox.Text);
            if (!processes.Contains(target.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                processes.Add(target.ProcessName);
                RuleProcessesBox.Text = string.Join(", ", processes);
            }
        };
        timer.Start();
    }

    // OpenAI

    private void LoadOpenAI()
    {
        var hasKey = !string.IsNullOrWhiteSpace(App.Current.Shared.Credentials.GetOpenAIApiKey());
        ApiKeyHint.Text = hasKey
            ? "Ein API Key ist im Windows Credential Manager gespeichert. Zum Ersetzen einen neuen eingeben."
            : "Noch kein API Key hinterlegt. Er wird im Windows Credential Manager gespeichert, nicht in einer Datei.";
        TranscriptionModelBox.Text = _settings.OpenAI.TranscriptionModel;
        SmartModelBox.Text = _settings.OpenAI.SmartProcessingModel;
        FastModeBox.IsChecked = string.Equals(_settings.OpenAI.SmartProcessingServiceTier, "fast", StringComparison.OrdinalIgnoreCase);
        LanguagesBox.Text = string.Join(", ", _settings.OpenAI.Languages);
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (key.Length == 0)
        {
            key = App.Current.Shared.Credentials.GetOpenAIApiKey() ?? string.Empty;
        }

        if (key.Length == 0)
        {
            ConnectionResult.Text = "Bitte zuerst einen API Key eingeben.";
            return;
        }

        TestConnectionButton.IsEnabled = false;
        ConnectionResult.Text = "Teste …";
        try
        {
            var openAI = JsonSettingsStore.Clone(_settings).OpenAI;
            openAI.TranscriptionModel = TranscriptionModelBox.Text.Trim();
            openAI.SmartProcessingModel = SmartModelBox.Text.Trim();
            ConnectionResult.Text = string.Join(Environment.NewLine, await OpenAIConnectionTester.TestAsync(key, openAI));
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    // Diagnostics

    private void LoadDiagnostics() =>
        LogLevelBox.SelectedItem = LogLevelBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals((string)i.Content, _settings.Diagnostics.LogLevel, StringComparison.OrdinalIgnoreCase))
            ?? LogLevelBox.Items[1];

    private void OnOpenLogs(object sender, RoutedEventArgs e) => TrayIcon.OpenLogs(App.Current.Log);

    private void OnDeleteLogs(object sender, RoutedEventArgs e)
    {
        var deleted = 0;
        if (Directory.Exists(AppPaths.LogDirectory))
        {
            foreach (var file in Directory.GetFiles(AppPaths.LogDirectory, "*.log"))
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException)
                {
                    // In use right now; it will be gone next time.
                }
            }
        }

        LogsResult.Text = deleted == 1 ? "1 Logdatei gelöscht." : $"{deleted} Logdateien gelöscht.";
    }

    // Save

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var errors = UpdateHotkeyWarnings();
        var silence = ReadInt(SilenceTimeoutBox, "Stille-Timeout", 0, 3600, errors);
        var timeout = ReadInt(TimeoutBox, "Timeout Transkription", 3, 300, errors);
        var smartTimeout = ReadInt(SmartTimeoutBox, "Timeout Smart Processing", 2, 120, errors);
        var retries = ReadInt(RetriesBox, "Wiederholungen", 0, 5, errors);
        if (string.IsNullOrWhiteSpace(TranscriptionModelBox.Text) || string.IsNullOrWhiteSpace(SmartModelBox.Text))
        {
            errors.Add("Beide OpenAI-Modelle müssen angegeben sein.");
        }

        if (_rules.Any(r => string.IsNullOrWhiteSpace(r.Name) || r.Processes.Count == 0))
        {
            errors.Add("Jede App-Regel braucht einen Namen und mindestens ein Programm.");
        }

        if (errors.Count > 0)
        {
            ValidationText.Text = string.Join(" ", errors);
            return;
        }

        _settings.General.Active = ActiveBox.IsChecked == true;
        _settings.General.Autostart = AutostartBox.IsChecked == true;

        _settings.Hotkeys.PushToTalk = PushToTalkBox.Gesture;
        _settings.Hotkeys.HandsFree = HandsFreeBox.Gesture;
        _settings.Hotkeys.ToggleSmartRaw = ToggleModeBox.Gesture;
        _settings.Hotkeys.AddToDictionary = AddToDictionaryBox.Gesture;

        _settings.Audio.InputDeviceName = (string)((ComboBoxItem)MicrophoneBox.SelectedItem).Tag;
        _settings.Audio.SoundsEnabled = SoundsBox.IsChecked == true;
        _settings.Audio.SoundVolume = Math.Round(VolumeSlider.Value, 2);
        _settings.Audio.HandsFreeSilenceTimeoutSeconds = silence;

        _settings.Processing.DefaultMode = (string)((ComboBoxItem)DefaultModeBox.SelectedItem).Tag;
        _settings.Processing.TimeoutSeconds = timeout;
        _settings.Processing.SmartTimeoutSeconds = smartTimeout;
        _settings.Processing.MaxRetries = retries;
        _settings.Processing.SkipSmartForSimpleText = SkipSimpleBox.IsChecked == true;
        _settings.Processing.SendWindowTitle = WindowTitleBox.IsChecked == true;

        _settings.OpenAI.TranscriptionModel = TranscriptionModelBox.Text.Trim();
        _settings.OpenAI.SmartProcessingModel = SmartModelBox.Text.Trim();
        _settings.OpenAI.SmartProcessingServiceTier = FastModeBox.IsChecked == true ? "fast" : "default";
        _settings.OpenAI.Languages = SplitList(LanguagesBox.Text).Select(l => l.ToLowerInvariant()).ToList();

        _settings.Diagnostics.LogLevel = (string)((ComboBoxItem)LogLevelBox.SelectedItem).Content;

        var newKey = ApiKeyBox.Password.Trim();
        if (newKey.Length > 0)
        {
            try
            {
                App.Current.Shared.Credentials.SetOpenAIApiKey(newKey);
                App.Current.Log.Info("OpenAI API key stored in Windows Credential Manager.");
            }
            catch (Exception ex)
            {
                ValidationText.Text = ex.Message;
                return;
            }
        }

        App.Current.ApplySettings(_settings);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private static int ReadInt(TextBox box, string name, int min, int max, List<string> errors)
    {
        if (int.TryParse(box.Text.Trim(), out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors.Add($"{name}: bitte eine Zahl von {min} bis {max} eingeben.");
        return min;
    }

    private static List<string> SplitList(string text) =>
        text.Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
