namespace Backend.IntegrationTests.Infrastructure;

internal static class EventualConsistencyTestSupport
{
    public static async Task EventuallyAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan pollInterval,
        string? because = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition())
                    return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(pollInterval);
        }

        var reason = because ?? "Condition was not met within timeout.";
        if (lastException is not null)
            throw new TimeoutException($"{reason} Last error: {lastException.Message}", lastException);

        throw new TimeoutException(reason);
    }
}
