namespace TKVoice.Infrastructure;

public static class AppPaths
{
    public static string SettingsFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TK Voice", "settings.json");

    public static string DictionaryFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TK Voice", "dictionary.json");

    public static string UsageFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TK Voice", "usage.json");

    /// <summary>Debug mode data (NFR-007), kept apart from the technical logs.</summary>
    public static string DebugDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TK Voice", "debug");

    public static string LogDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TK Voice", "logs");
}
