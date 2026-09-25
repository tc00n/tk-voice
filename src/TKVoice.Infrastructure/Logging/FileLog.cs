using TKVoice.Core.Abstractions;

namespace TKVoice.Infrastructure.Logging;

/// <summary>Daily technical log file. Callers must not pass content or secrets (NFR-006).</summary>
public sealed class FileLog(string directory, string minimumLevel) : ILog
{
    private readonly Lock _gate = new();
    private int _minimum = Rank(minimumLevel);

    /// <summary>Debug, Info, Warn or Error; can be changed at runtime.</summary>
    public void SetMinimumLevel(string level) => _minimum = Rank(level);

    public string Directory { get; } = directory;

    public void Debug(string message) => Write("DEBUG", message);

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    private void Write(string level, string message)
    {
        if (Rank(level) < _minimum)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
        try
        {
            lock (_gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(Path.Combine(Directory, $"tkvoice-{now:yyyyMMdd}.log"), line);
            }
        }
        catch (IOException)
        {
            // Logging must never break dictation.
        }
    }

    private static int Rank(string level) => level.ToUpperInvariant() switch
    {
        "DEBUG" => 0,
        "INFO" => 1,
        "WARN" => 2,
        "ERROR" => 3,
        _ => 1,
    };
}
