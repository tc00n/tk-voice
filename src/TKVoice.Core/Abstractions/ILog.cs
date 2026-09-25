namespace TKVoice.Core.Abstractions;

/// <summary>
/// Technical log. Must never receive audio, transcripts, dictated text or secrets (NFR-006).
/// </summary>
public interface ILog
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}
