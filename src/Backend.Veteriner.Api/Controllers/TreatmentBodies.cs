namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /api/v1/treatments/{id} body; route id is source of truth.</summary>
public sealed class UpdateTreatmentBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public Guid ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public DateTime TreatmentDateUtc { get; init; }
    public string Title { get; init; } = default!;
    public string Description { get; init; } = default!;
    public string? Notes { get; init; }
    public DateTime? FollowUpDateUtc { get; init; }
}
