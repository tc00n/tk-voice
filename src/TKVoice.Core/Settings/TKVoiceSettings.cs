namespace TKVoice.Core.Settings;

/// <summary>
/// Persisted configuration (NFR-004). Must never contain dictated content or the API key.
/// </summary>
public sealed class TKVoiceSettings
{
    public HotkeySettings Hotkeys { get; set; } = new();
    public AudioSettings Audio { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public OpenAISettings OpenAI { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
}

public sealed class HotkeySettings
{
    public string PushToTalk { get; set; } = "RightCtrl";
}

public sealed class AudioSettings
{
    /// <summary>WaveIn device number; -1 uses the Windows default microphone.</summary>
    public int InputDeviceNumber { get; set; } = -1;

    /// <summary>Recordings shorter than this are treated as accidental taps and discarded.</summary>
    public int MinimumRecordingMilliseconds { get; set; } = 300;
}

public sealed class ProcessingSettings
{
    /// <summary>Maximum time from end of recording until the final transcript must be available.</summary>
    public int TimeoutSeconds { get; set; } = 20;
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
}

public sealed class DiagnosticsSettings
{
    /// <summary>Debug, Info, Warn or Error.</summary>
    public string LogLevel { get; set; } = "Info";
}
