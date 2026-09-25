namespace TKVoice.Core.Abstractions;

/// <summary>
/// Reads the text the user has deliberately selected in the foreground application (FR-020).
/// Must read nothing else and leave the clipboard as it was.
/// </summary>
public interface ISelectionReader
{
    /// <summary>Returns the selected text, or null if nothing is selected or it could not be read.</summary>
    Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken);
}
