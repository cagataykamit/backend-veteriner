namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

public sealed class ClinicClientActivityReadModel
{
    public Guid TenantId { get; set; }
    public Guid ClinicId { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = default!;
    public string? ClientPhone { get; set; }
    public DateTime LastAppointmentAtUtc { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }
}
