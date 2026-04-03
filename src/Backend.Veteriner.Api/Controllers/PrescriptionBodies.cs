namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /api/v1/prescriptions/{id} body; route id is source of truth.</summary>
public sealed class UpdatePrescriptionBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public Guid ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public Guid? TreatmentId { get; init; }
    public DateTime PrescribedAtUtc { get; init; }
    public string Title { get; init; } = default!;
    public string Content { get; init; } = default!;
    public string? Notes { get; init; }
    public DateTime? FollowUpDateUtc { get; init; }
}
