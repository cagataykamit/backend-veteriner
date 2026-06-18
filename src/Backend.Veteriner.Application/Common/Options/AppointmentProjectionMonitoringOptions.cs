namespace Backend.Veteriner.Application.Common.Options;

public sealed class AppointmentProjectionMonitoringOptions
{
    public const string SectionName = "AppointmentProjectionMonitoring";

    public int WarningPendingAgeSeconds { get; set; } = 10;

    public int CriticalPendingAgeSeconds { get; set; } = 30;

    public bool ParityCheckEnabled { get; set; }

    public int ParityCheckIntervalSeconds { get; set; } = 300;
}
