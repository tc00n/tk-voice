namespace TKVoice.Core.Processing;

/// <summary>
/// Everything the smart processing model may receive (§42). Deliberately excludes any content of
/// the target application, other windows or the clipboard.
/// </summary>
/// <param name="Transcript">Final transcript of the dictation.</param>
/// <param name="ApplicationName">Target application, e.g. "Microsoft Outlook (OUTLOOK)".</param>
/// <param name="WindowTitle">Only set when explicitly enabled in the settings.</param>
/// <param name="AppStyle">Style guidance from the matching app rule, if any (FR-024).</param>
public sealed record TextProcessingRequest(string Transcript, string ApplicationName, string? WindowTitle = null, string? AppStyle = null);

/// <summary>Turns a final transcript into the text to insert (Smart processing, §42/§43).</summary>
public interface ISmartTextProcessor
{
    Task<string> ProcessAsync(TextProcessingRequest request, CancellationToken cancellationToken);

    /// <summary>Prepares the connection while the user is still speaking. Fire-and-forget.</summary>
    void Warmup()
    {
    }
}

public sealed class SmartProcessingException(string message, Exception? inner = null) : Exception(message, inner);
