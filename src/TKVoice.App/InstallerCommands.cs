using System.Diagnostics;
using System.IO;
using TKVoice.Infrastructure;

namespace TKVoice.App;

/// <summary>
/// Command-line hooks for the installer (Phase 11). Handled before the single-instance check.
/// <c>--quit</c> ends a running TK Voice so files can be replaced; <c>--uninstall</c> additionally
/// removes the autostart entry, technical logs and debug data. Settings, dictionary, usage and the
/// API key stay, so a reinstall keeps the user's configuration.
/// </summary>
internal static class InstallerCommands
{
    public const string QuitEventName = @"Local\TKVoice.Quit";

    /// <summary>Returns true if the arguments were an installer command (the process should then exit).</summary>
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            QuitRunningInstance();
            return true;
        }

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            QuitRunningInstance();
            Cleanup();
            return true;
        }

        return false;
    }

    /// <summary>Lets a running instance react to <c>--quit</c> by invoking <paramref name="shutdown"/>.</summary>
    public static void ListenForQuit(Action shutdown)
    {
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        new Thread(() =>
        {
            quit.WaitOne();
            shutdown();
        })
        { IsBackground = true, Name = "TKVoice quit listener" }.Start();
    }

    private static void QuitRunningInstance()
    {
        if (EventWaitHandle.TryOpenExisting(QuitEventName, out var quit))
        {
            quit.Set();
            quit.Dispose();
        }

        foreach (var process in Process.GetProcessesByName("TKVoice").Where(p => p.Id != Environment.ProcessId))
        {
            try
            {
                if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
                // Already gone or not ours to stop.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void Cleanup()
    {
        TryRun(() => AutostartService.Apply(false));
        TryRun(() => Directory.Delete(AppPaths.LogDirectory, recursive: true));
        TryRun(() => Directory.Delete(AppPaths.DebugDirectory, recursive: true));
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Best effort; uninstall must not fail because of leftovers.
        }
    }
}
