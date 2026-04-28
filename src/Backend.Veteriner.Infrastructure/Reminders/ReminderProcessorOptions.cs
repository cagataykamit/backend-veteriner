namespace Backend.Veteriner.Infrastructure.Reminders;

public sealed class ReminderProcessorOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalMinutes { get; init; } = 5;
    public int BatchSize { get; init; } = 100;
    public int WindowToleranceMinutes { get; init; } = 10;
}
