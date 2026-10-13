namespace Backend.Veteriner.Application.Common.Options;

public sealed class OutboxHealthOptions
{
    public const string SectionName = "OutboxHealth";

    /// <summary>Bekleyen mesaj sayısı bu değeri aşarsa Degraded.</summary>
    public int PendingDegradedThreshold { get; set; } = 500;

    public int DegradedAfterSeconds { get; set; } = 120;

    public int UnhealthyAfterSeconds { get; set; } = 600;

    public bool DeadLetterIsUnhealthy { get; set; } = true;
}
