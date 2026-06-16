namespace Backend.Veteriner.Infrastructure.Persistence.Query.Models;

public sealed class ClinicPetActivityReadModel
{
    public Guid TenantId { get; set; }
    public Guid ClinicId { get; set; }
    public Guid PetId { get; set; }
    public Guid ClientId { get; set; }
    public string PetName { get; set; } = default!;
    public Guid SpeciesId { get; set; }
    public string SpeciesName { get; set; } = default!;
    public DateTime LastAppointmentAtUtc { get; set; }
    public Guid LastEventId { get; set; }
    public DateTime LastProjectedAtUtc { get; set; }
}
