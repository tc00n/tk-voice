namespace TKVoice.Core.Abstractions;

/// <summary>Detects whether keyboard focus is in a password field (FR-038). Reads no content.</summary>
public interface IPasswordFieldDetector
{
    /// <summary>
    /// True only if the focused element clearly identifies itself as a password field. Unknown or
    /// slow-to-answer applications count as "not a password field".
    /// </summary>
    bool IsFocusInPasswordField();
}

public sealed class NoPasswordFieldDetector : IPasswordFieldDetector
{
    public bool IsFocusInPasswordField() => false;
}
