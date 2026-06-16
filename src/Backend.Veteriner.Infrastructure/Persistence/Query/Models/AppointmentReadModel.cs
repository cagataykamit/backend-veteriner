namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

public sealed class AppointmentReadModel
{
    public Guid AppointmentId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ClinicId { get; set; }
    public string ClinicName { get; set; } = default!;
    public Guid PetId { get; set; }
    public string PetName { get; set; } = default!;
    public Guid SpeciesId { get; set; }
    public string SpeciesName { get; set; } = default!;
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = default!;
    public string? ClientPhone { get; set; }
    public string? ClientPhoneNormalized { get; set; }
    public string? ClientEmail { get; set; }
    public string? PetBreed { get; set; }
    public string? PetBreedRefName { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime ScheduledEndUtc { get; set; }
    public int DurationMinutes { get; set; }
    public int AppointmentType { get; set; }
    public int Status { get; set; }
    public string? Notes { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }
}
