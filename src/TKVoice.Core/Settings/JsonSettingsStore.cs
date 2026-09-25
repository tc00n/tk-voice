using System.Text.Json;

namespace TKVoice.Core.Settings;

public sealed class JsonSettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string FilePath { get; } = filePath;

    /// <summary>Loads settings, writing a default file first if none exists.</summary>
    public TKVoiceSettings LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new TKVoiceSettings();
            Save(defaults);
            return defaults;
        }

        using var stream = File.OpenRead(FilePath);
        return JsonSerializer.Deserialize<TKVoiceSettings>(stream, Options) ?? new TKVoiceSettings();
    }

    public void Save(TKVoiceSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, Options));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
