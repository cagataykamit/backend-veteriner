using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Hospitalizations;

/// <summary>
/// In-clinic hospitalization / observation stay for a pet; optionally linked to an examination.
/// </summary>
public sealed class Hospitalization : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public DateTime AdmittedAtUtc { get; private set; }
    public DateTime? PlannedDischargeAtUtc { get; private set; }
    public DateTime? DischargedAtUtc { get; private set; }
    public string Reason { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private Hospitalization() { }

    public Hospitalization(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime admittedAtUtc,
        DateTime? plannedDischargeAtUtc,
        string reason,
        string? notes)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId is invalid.", nameof(petId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        var admitted = NormalizeUtc(admittedAtUtc);
        if (plannedDischargeAtUtc.HasValue && NormalizeUtc(plannedDischargeAtUtc.Value) < admitted)
            throw new ArgumentException("Planned discharge must not be before admission.", nameof(plannedDischargeAtUtc));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        AdmittedAtUtc = admitted;
        PlannedDischargeAtUtc = plannedDischargeAtUtc.HasValue ? NormalizeUtc(plannedDischargeAtUtc.Value) : null;
        DischargedAtUtc = null;
        Reason = reason.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    public Result UpdateDetails(
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime admittedAtUtc,
        DateTime? plannedDischargeAtUtc,
        string reason,
        string? notes)
    {
        if (DischargedAtUtc.HasValue)
            return Result.Failure("Hospitalizations.AlreadyDischarged", "Discharged hospitalization cannot be updated.");

        if (clinicId == Guid.Empty)
            return Result.Failure("Hospitalizations.Validation", "ClinicId is invalid.");
        if (petId == Guid.Empty)
            return Result.Failure("Hospitalizations.Validation", "PetId is invalid.");
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure("Hospitalizations.Validation", "Reason is required.");

        var admitted = NormalizeUtc(admittedAtUtc);
        if (plannedDischargeAtUtc.HasValue && NormalizeUtc(plannedDischargeAtUtc.Value) < admitted)
        {
            return Result.Failure(
                "Hospitalizations.PlannedDischargeBeforeAdmission",
                "Planned discharge must not be before admission.");
        }

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        AdmittedAtUtc = admitted;
        PlannedDischargeAtUtc = plannedDischargeAtUtc.HasValue ? NormalizeUtc(plannedDischargeAtUtc.Value) : null;
        Reason = reason.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Discharge(DateTime dischargedAtUtc, bool applyNotes, string? notes)
    {
        if (DischargedAtUtc.HasValue)
            return Result.Failure("Hospitalizations.AlreadyDischarged", "Hospitalization is already discharged.");

        var d = NormalizeUtc(dischargedAtUtc);
        if (d < AdmittedAtUtc)
        {
            return Result.Failure(
                "Hospitalizations.DischargedBeforeAdmission",
                "Discharge time must not be before admission.");
        }

        DischargedAtUtc = d;
        if (applyNotes)
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
