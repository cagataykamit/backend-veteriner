namespace Backend.Veteriner.Application.Common.Options;

public sealed class ClientProjectionHealthOptions
{
    public const string SectionName = "ClientProjectionHealth";

    public int DegradedAfterSeconds { get; set; } = 10;

    public int UnhealthyAfterSeconds { get; set; } = 30;

    public bool DeadLetterIsUnhealthy { get; set; } = true;
}
