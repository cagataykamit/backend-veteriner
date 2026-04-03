namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /api/v1/hospitalizations/{id} body; route id is source of truth.</summary>
public sealed class UpdateHospitalizationBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public Guid ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }
    public DateTime AdmittedAtUtc { get; init; }
    public DateTime? PlannedDischargeAtUtc { get; init; }
    public string Reason { get; init; } = default!;
    public string? Notes { get; init; }
}

/// <summary>POST /api/v1/hospitalizations/{id}/discharge body.</summary>
public sealed class DischargeHospitalizationBody
{
    public DateTime DischargedAtUtc { get; init; }

    /// <summary>
    /// When set (including empty string), replaces stored notes after trim; omit to leave notes unchanged.
    /// </summary>
    public string? Notes { get; init; }
}
