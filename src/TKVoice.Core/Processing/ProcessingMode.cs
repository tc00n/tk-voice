namespace TKVoice.Core.Processing;

public enum ProcessingMode
{
    /// <summary>Cleaned up and formatted by the smart processing model (default, FR-008).</summary>
    Smart,

    /// <summary>The transcript as recognized, without rewriting (FR-007).</summary>
    Raw,
}

/// <summary>The currently active mode; switchable at runtime via hotkey or tray (FR-039). Thread-safe.</summary>
public sealed class ProcessingModeState(ProcessingMode initial)
{
    private int _mode = (int)initial;

    public event EventHandler<ProcessingMode>? Changed;

    public ProcessingMode Current => (ProcessingMode)Volatile.Read(ref _mode);

    public ProcessingMode Toggle()
    {
        int current, next;
        do
        {
            current = Volatile.Read(ref _mode);
            next = current == (int)ProcessingMode.Smart ? (int)ProcessingMode.Raw : (int)ProcessingMode.Smart;
        }
        while (Interlocked.CompareExchange(ref _mode, next, current) != current);

        Changed?.Invoke(this, (ProcessingMode)next);
        return (ProcessingMode)next;
    }
}
