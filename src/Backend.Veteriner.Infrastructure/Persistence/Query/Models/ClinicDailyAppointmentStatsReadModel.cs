namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

public sealed class ClinicDailyAppointmentStatsReadModel
{
    public Guid TenantId { get; set; }
    public Guid ClinicId { get; set; }
    public DateOnly LocalDate { get; set; }
    public int ScheduledCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public int TotalCount { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }
}
