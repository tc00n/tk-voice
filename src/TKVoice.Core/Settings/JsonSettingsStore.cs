using System.Text.Json;

namespace TKVoice.Core.Settings;

public sealed class JsonSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string FilePath { get; } = filePath;

    /// <summary>
    /// Loads settings and writes them back, so the file always lists every option, including ones
    /// added in newer versions (with their defaults).
    /// </summary>
    public TKVoiceSettings LoadOrCreate()
    {
        var settings = new TKVoiceSettings();
        if (File.Exists(FilePath))
        {
            using var stream = File.OpenRead(FilePath);
            settings = JsonSerializer.Deserialize<TKVoiceSettings>(stream, Options) ?? settings;
        }

        Save(settings);
        return settings;
    }

    /// <summary>Deep copy, e.g. for editing in the settings window without touching the live settings.</summary>
    public static TKVoiceSettings Clone(TKVoiceSettings settings) =>
        JsonSerializer.Deserialize<TKVoiceSettings>(JsonSerializer.Serialize(settings, Options), Options)!;

    public void Save(TKVoiceSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, Options));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
