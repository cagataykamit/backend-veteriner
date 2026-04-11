namespace Backend.Veteriner.Infrastructure.Billing;

public sealed class ScheduledPlanChangeProcessorOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 60;
    public int BatchSize { get; init; } = 100;
}
