using Microsoft.Win32;

namespace TKVoice.Infrastructure;

/// <summary>Start with Windows via the per-user Run key (FR-042); no admin rights needed.</summary>
public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TK Voice";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command && command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName);
        }
    }

    private static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unknown.");
}
