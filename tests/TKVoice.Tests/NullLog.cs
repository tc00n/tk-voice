using TKVoice.Core.Abstractions;

namespace TKVoice.Tests;

internal sealed class NullLog : ILog
{
    public void Debug(string message)
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}
