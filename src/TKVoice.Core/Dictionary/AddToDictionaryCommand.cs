using TKVoice.Core.Abstractions;

namespace TKVoice.Core.Dictionary;

/// <summary>"Zum Wörterbuch hinzufügen" (FR-020): adds the currently selected term via hotkey.</summary>
public sealed class AddToDictionaryCommand(
    ISelectionReader selectionReader,
    PersonalDictionary dictionary,
    IUserNotifier notifier,
    ILog log)
{
    private int _running;

    /// <summary>Completes when the most recent run has finished. Intended for tests.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Execute()
    {
        // Ignore repeated hotkey presses while a read is in progress.
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        Completion = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var selection = await selectionReader.ReadSelectedTextAsync(timeout.Token);
                if (!PersonalDictionary.TryNormalize(selection, out var term, out var error))
                {
                    notifier.ShowError(error);
                    return;
                }

                if (dictionary.Add(term))
                {
                    log.Info($"Dictionary entry added via selection ({term.Length} chars).");
                    notifier.ShowInfo($"„{term}“ zum Wörterbuch hinzugefügt");
                }
                else
                {
                    notifier.ShowInfo($"„{term}“ ist bereits im Wörterbuch");
                }
            }
            catch (Exception ex)
            {
                log.Error("Adding selection to dictionary failed.", ex);
                notifier.ShowError("Markierter Begriff konnte nicht gelesen werden.");
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }
}
