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

    /// <summary>Base64 version from GET; required (ADR-043 K5).</summary>
    public string? RowVersion { get; init; }
}

/// <summary>POST /api/v1/treatments/{id}/cancel body; reason 5-500 characters, rowVersion required.</summary>
public sealed class CancelTreatmentBody
{
    public string? Reason { get; init; }
    public string? RowVersion { get; init; }
}
