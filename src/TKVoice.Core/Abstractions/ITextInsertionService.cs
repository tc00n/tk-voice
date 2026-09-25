namespace TKVoice.Core.Abstractions;

public enum InsertionResult
{
    Inserted,

    /// <summary>The original target no longer exists or could not be reliably refocused (FR-027).</summary>
    TargetUnavailable,

    /// <summary>Focus is in a password field; insertion blocked (FR-038).</summary>
    PasswordField,
}

public interface ITextInsertionService
{
    Task<InsertionResult> InsertAsync(DictationTarget target, string text, CancellationToken cancellationToken);
}
