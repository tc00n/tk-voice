using TKVoice.Core.Abstractions;

namespace TKVoice.Core.Reliability;

/// <summary>
/// Retry with a timeout per attempt (FR-035/FR-036): transient failures and attempt timeouts are
/// retried up to <c>maxRetries</c> times with a short backoff; anything else fails immediately.
/// A final attempt timeout surfaces as <see cref="TimeoutException"/>.
/// </summary>
public static class Retry
{
    public static async Task<T> RunAsync<T>(
        string operation,
        Func<int, CancellationToken, Task<T>> attempt,
        int maxRetries,
        TimeSpan attemptTimeout,
        Func<Exception, bool> isTransient,
        ILog log,
        CancellationToken cancellationToken,
        Func<int, TimeSpan>? backoff = null)
    {
        backoff ??= DefaultBackoff;
        for (var attemptNumber = 0; ; attemptNumber++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(attemptTimeout);
            Exception failure;
            try
            {
                return await attempt(attemptNumber, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                failure = new TimeoutException($"{operation}: keine Antwort innerhalb von {attemptTimeout.TotalSeconds:F0} s.");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && isTransient(ex))
            {
                failure = ex;
            }

            if (attemptNumber >= maxRetries)
            {
                log.Warn($"{operation} failed after {attemptNumber + 1} attempts ({failure.GetType().Name}).");
                throw failure;
            }

            log.Warn($"{operation} attempt {attemptNumber + 1} failed ({failure.GetType().Name}); retrying.");
            await Task.Delay(backoff(attemptNumber), cancellationToken);
        }
    }

    /// <summary>0.5 s, 2 s, 4.5 s, …</summary>
    public static TimeSpan DefaultBackoff(int attemptNumber) => TimeSpan.FromMilliseconds(500 * (attemptNumber + 1) * (attemptNumber + 1));
}
