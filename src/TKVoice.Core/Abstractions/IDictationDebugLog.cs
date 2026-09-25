using TKVoice.Core.Processing;

namespace TKVoice.Core.Abstractions;

/// <summary>
/// Debug mode (NFR-007): records dictated content for troubleshooting. Off by default, visibly
/// indicated while on, and its data can be deleted with one click. Never used by the normal log.
/// </summary>
public interface IDictationDebugLog
{
    void Record(string application, ProcessingMode mode, string transcript, string insertedText);
}

public sealed class NoDictationDebugLog : IDictationDebugLog
{
    public void Record(string application, ProcessingMode mode, string transcript, string insertedText)
    {
    }
}
