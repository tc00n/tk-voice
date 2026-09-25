using TKVoice.Core.Processing;

namespace TKVoice.Core.Settings;

/// <summary>
/// Persisted configuration (NFR-004). Must never contain dictated content or the API key.
/// </summary>
public sealed class TKVoiceSettings
{
    public HotkeySettings Hotkeys { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public InsertionSettings Insertion { get; set; } = new();
    public DictionarySettings Dictionary { get; set; } = new();

    /// <summary>App-specific smart processing styles (FR-024), matched by process name.</summary>
    public List<AppRule> AppRules { get; set; } = AppRule.Defaults();
    public OpenAISettings OpenAI { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
}

public sealed class HotkeySettings
{
    public string PushToTalk { get; set; } = "RightCtrl";

    /// <summary>Switches between Smart and Raw mode (FR-039). Empty disables the hotkey.</summary>
    public string ToggleSmartRaw { get; set; } = "Ctrl+Shift+F12";

    /// <summary>Adds the selected text to the personal dictionary (FR-020). Empty disables the hotkey.</summary>
    public string AddToDictionary { get; set; } = "Ctrl+Shift+F11";
}

public sealed class AudioSettings
{
    /// <summary>WaveIn device number; -1 uses the Windows default microphone.</summary>
    public int InputDeviceNumber { get; set; } = -1;

    /// <summary>Recordings shorter than this are treated as accidental taps and discarded.</summary>
    public int MinimumRecordingMilliseconds { get; set; } = 300;

    public bool SoundsEnabled { get; set; } = true;

    /// <summary>Volume of the start/stop sounds, 0.0 to 1.0.</summary>
    public double SoundVolume { get; set; } = 0.4;
}

public sealed class ProcessingSettings
{
    /// <summary>Maximum time from end of recording until the final transcript must be available.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>"Smart" or "Raw"; the mode TK Voice starts in.</summary>
    public string DefaultMode { get; set; } = "Smart";

    /// <summary>Maximum time for the smart processing request; on failure the raw transcript is inserted.</summary>
    public int SmartTimeoutSeconds { get; set; } = 8;

    /// <summary>Skip smart processing for short transcripts without fillers, corrections, commands or numbers.</summary>
    public bool SkipSmartForSimpleText { get; set; } = true;

    /// <summary>Also send the target window title as context. Off by default: titles can contain e-mail subjects etc.</summary>
    public bool SendWindowTitle { get; set; }
}

public sealed class DictionarySettings
{
    /// <summary>Learn terms spelled out during dictation ("NEONEX, geschrieben N-E-O-N-E-X"), FR-021.</summary>
    public bool LearnSpelledTerms { get; set; } = true;

    /// <summary>At most this many terms (the most recently added) are sent as transcription keywords.</summary>
    public int MaxTranscriptionKeywords { get; set; } = 100;
}

public sealed class InsertionSettings
{
    /// <summary>"Paste" (clipboard + Ctrl+V, clipboard restored afterwards) or "Type" (Unicode keystrokes).</summary>
    public string Method { get; set; } = "Paste";

    /// <summary>Process names (without .exe) that should always receive typed keystrokes instead of a paste.</summary>
    public List<string> TypeInsteadOfPasteProcesses { get; set; } = [];

    /// <summary>
    /// Grace period after the target application has read the pasted text before the previous
    /// clipboard content is restored.
    /// </summary>
    public int ClipboardRestoreDelayMilliseconds { get; set; } = 150;
}

public sealed class OpenAISettings
{
    public string RealtimeUrl { get; set; } = "wss://api.openai.com/v1/realtime?intent=transcription";
    public string TranscriptionModel { get; set; } = "gpt-live-transcribe";

    /// <summary>Latency/accuracy trade-off of the transcription model: minimal, low, medium, high, xhigh.</summary>
    public string TranscriptionDelay { get; set; } = "low";

    /// <summary>ISO 639-1 language hints. Empty lets the model detect freely.</summary>
    public List<string> Languages { get; set; } = ["de", "en"];

    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>Silence appended before the final commit so the last spoken word is not cut off.</summary>
    public int TrailingSilenceMilliseconds { get; set; } = 300;

    public string ResponsesUrl { get; set; } = "https://api.openai.com/v1/responses";
    public string SmartProcessingModel { get; set; } = "gpt-6-luna";

    /// <summary>Reasoning effort of the smart processing model; "none" keeps latency lowest.</summary>
    public string SmartProcessingReasoningEffort { get; set; } = "none";

    /// <summary>"fast" (OpenAI Fast mode: lower latency, 2× price) or "default"/empty for standard processing.</summary>
    public string SmartProcessingServiceTier { get; set; } = "fast";
}

public sealed class DiagnosticsSettings
{
    /// <summary>Debug, Info, Warn or Error.</summary>
    public string LogLevel { get; set; } = "Info";
}
