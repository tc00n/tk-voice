using TKVoice.Core.Reliability;

namespace TKVoice.Tests.Reliability;

public class RetryTests
{
    private static Task<int> Run(Func<int, CancellationToken, Task<int>> attempt, int maxRetries = 2, int timeoutMs = 1000) =>
        Retry.RunAsync("Test", attempt, maxRetries, TimeSpan.FromMilliseconds(timeoutMs), ex => ex is IOException, new NullLog(),
            CancellationToken.None, _ => TimeSpan.Zero);

    [Fact]
    public async Task Succeeds_after_transient_failures()
    {
        var calls = 0;
        var result = await Run((_, _) => ++calls < 3 ? Task.FromException<int>(new IOException()) : Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Non_transient_failure_is_not_retried()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run((_, _) =>
            {
                calls++;
                return Task.FromException<int>(new InvalidOperationException());
            }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Final_timeout_becomes_timeout_exception()
    {
        var calls = 0;
        await Assert.ThrowsAsync<TimeoutException>(() =>
            Run(async (_, token) =>
            {
                calls++;
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            }, maxRetries: 1, timeoutMs: 50));

        Assert.Equal(2, calls);
    }
}
