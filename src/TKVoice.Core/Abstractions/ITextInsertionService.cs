namespace TKVoice.Core.Abstractions;

public enum InsertionResult
{
    Inserted,

    /// <summary>The original target no longer exists or could not be reliably refocused (FR-027).</summary>
    TargetUnavailable,
}

public interface ITextInsertionService
{
    Task<InsertionResult> InsertAsync(DictationTarget target, string text, CancellationToken cancellationToken);
}
