namespace Backend.Veteriner.Application.Common.Options;

public sealed class AppointmentProjectionHealthOptions
{
    public const string SectionName = "AppointmentProjectionHealth";

    public int DegradedAfterSeconds { get; set; } = 10;

    public int UnhealthyAfterSeconds { get; set; } = 30;

    public bool DeadLetterIsUnhealthy { get; set; } = true;
}
