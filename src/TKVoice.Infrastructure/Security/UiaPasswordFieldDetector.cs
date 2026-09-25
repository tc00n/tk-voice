using System.Windows.Automation;
using TKVoice.Core.Abstractions;

namespace TKVoice.Infrastructure.Security;

/// <summary>
/// Asks UI Automation whether the focused element is a password field (FR-038). Covers Win32
/// password edits, WPF/WinUI PasswordBoxes and browser &lt;input type="password"&gt;. Only this one
/// flag is read, never the field's content. Unresponsive applications must not delay dictation, so
/// the query has a short time limit and counts as "no password field" when exceeded.
/// </summary>
public sealed class UiaPasswordFieldDetector(ILog log) : IPasswordFieldDetector
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMilliseconds(250);

    public bool IsFocusInPasswordField()
    {
        var query = Task.Run(() =>
        {
            try
            {
                return AutomationElement.FocusedElement?.Current.IsPassword == true;
            }
            catch (Exception)
            {
                // Element vanished or the application does not support UI Automation.
                return false;
            }
        });

        if (query.Wait(QueryTimeout))
        {
            return query.Result;
        }

        log.Debug("Password field check timed out.");
        return false;
    }

    /// <summary>The first UI Automation call loads the client; do it once in the background at startup.</summary>
    public void Warmup() => _ = Task.Run(IsFocusInPasswordField);
}
