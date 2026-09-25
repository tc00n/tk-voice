using System.Text;
using TKVoice.Core.Abstractions;
using TKVoice.Core.Processing;

namespace TKVoice.Infrastructure.Logging;

/// <summary>
/// Debug mode storage (NFR-007): one file per day in its own folder, separate from the technical log,
/// so it can be deleted with one click. Only written while debug mode is switched on.
/// </summary>
public sealed class FileDictationDebugLog(string directory) : IDictationDebugLog
{
    private readonly Lock _gate = new();

    public static void DeleteAll(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public void Record(string application, ProcessingMode mode, string transcript, string insertedText)
    {
        var entry = new StringBuilder()
            .AppendLine($"=== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} · {application} · {mode}")
            .AppendLine("--- Transkript")
            .AppendLine(transcript)
            .AppendLine("--- Eingefügt")
            .AppendLine(insertedText)
            .AppendLine()
            .ToString();

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, $"debug-{DateTimeOffset.Now:yyyyMMdd}.txt"), entry);
            }
        }
        catch (IOException)
        {
            // Debug output must never break dictation.
        }
    }
}
