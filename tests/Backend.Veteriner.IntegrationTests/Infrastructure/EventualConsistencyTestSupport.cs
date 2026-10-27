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

    /// <summary>
    /// Sunucunun yazdığı zaman damgalarına göre sıralanan kayıtlarda sıra belirgin olsun: yerel saat verilen anı (1 ms
    /// payla) geçene kadar yoklar. Sunucu ile test aynı süreçte, aynı saati kullanır.
    /// </summary>
    public static Task UntilClockPassesAsync(DateTime utc)
        => EventuallyAsync(
            () => Task.FromResult(DateTime.UtcNow > utc.AddMilliseconds(1)),
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(1),
            because: "Saat verilen anı geçmedi.");
}
