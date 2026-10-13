namespace Backend.Veteriner.Api.Controllers;

/// <summary>PUT /api/v1/hospitalizations/{id} body; route id is source of truth.</summary>
public sealed class UpdateHospitalizationBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public Guid ClinicId { get; init; }
    public Guid PetId { get; init; }
    public Guid? ExaminationId { get; init; }

    /// <summary>Omit to keep the current visit link (or inherit it from a changed examination).</summary>
    public Guid? VisitId { get; init; }
    public DateTime AdmittedAtUtc { get; init; }
    public DateTime? PlannedDischargeAtUtc { get; init; }
    public string Reason { get; init; } = default!;
    public string? Notes { get; init; }

    /// <summary>Free-text location; omit to leave unchanged, empty string clears.</summary>
    public string? Location { get; init; }

    /// <summary>Base64 version from GET; optional during the transition period, checked when sent.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>POST /api/v1/hospitalizations/{id}/discharge body.</summary>
public sealed class DischargeHospitalizationBody
{
    public DateTime DischargedAtUtc { get; init; }

    /// <summary>
    /// Discharge note; omit to leave unchanged, empty string clears. Never overwrites the admission notes.
    /// </summary>
    public string? DischargeNote { get; init; }

    /// <summary>Legacy name of <see cref="DischargeNote"/>; used only when <see cref="DischargeNote"/> is omitted.</summary>
    public string? Notes { get; init; }

    /// <summary>Base64 version from GET; optional during the transition period, checked when sent.</summary>
    public string? RowVersion { get; init; }
}
