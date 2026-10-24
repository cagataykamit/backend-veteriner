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

    /// <summary>Free-text location; omit to leave unchanged, empty string clears. Ignored when <see cref="RoomId"/> is set.</summary>
    public string? Location { get; init; }

    /// <summary>
    /// Clinic-defined room (HOSP-002); omit to keep the current room, set to assign (location text is derived as "Ward / Room").
    /// Must belong to the hospitalization clinic and be active unless it is the room already assigned.
    /// </summary>
    public Guid? RoomId { get; init; }

    /// <summary>Removes the room link; cannot be combined with <see cref="RoomId"/>. Location text follows the <see cref="Location"/> rule.</summary>
    public bool ClearRoom { get; init; }

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
