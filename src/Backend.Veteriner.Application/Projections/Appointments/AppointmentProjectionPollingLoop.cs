namespace Backend.Veteriner.Application.Projections.Appointments;

/// <summary>
/// Appointment projection hosted service polling kararları.
/// </summary>
public static class AppointmentProjectionPollingLoop
{
    public const int DefaultActiveFollowUpWindowSeconds = 5;
    public const int DefaultActiveFollowUpPollMilliseconds = 100;

    /// <summary>
    /// Batch en az bir event işlediyse hemen sonraki batch denenir; aksi halde idle beklenir.
    /// </summary>
    public static bool ShouldIdleWaitAfterBatch(int processedCount) => processedCount <= 0;

    public static TimeSpan ResolveIdleInterval(int loopIntervalSeconds)
        => TimeSpan.FromSeconds(Math.Max(1, loopIntervalSeconds));

    /// <summary>
    /// Son aktiviteden sonra kısa pencerede düşük gecikmeli poll; aksi halde tam idle interval.
    /// </summary>
    public static TimeSpan ResolveIdleDelay(
        int processedCount,
        DateTimeOffset? lastActivityUtc,
        DateTimeOffset now,
        int loopIntervalSeconds,
        int activeFollowUpWindowSeconds = DefaultActiveFollowUpWindowSeconds,
        int activeFollowUpPollMilliseconds = DefaultActiveFollowUpPollMilliseconds)
    {
        if (!ShouldIdleWaitAfterBatch(processedCount))
            return TimeSpan.Zero;

        if (lastActivityUtc is { } lastActivity &&
            now - lastActivity < TimeSpan.FromSeconds(Math.Max(1, activeFollowUpWindowSeconds)))
        {
            return TimeSpan.FromMilliseconds(Math.Max(50, activeFollowUpPollMilliseconds));
        }

        return ResolveIdleInterval(loopIntervalSeconds);
    }
}
