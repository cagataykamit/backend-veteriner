using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Prescriptions;

/// <summary>
/// Clinical prescription record for a pet; optionally linked to an examination and/or a treatment.
/// </summary>
public sealed class Prescription : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public Guid? TreatmentId { get; private set; }
    public DateTime PrescribedAtUtc { get; private set; }
    public string Title { get; private set; } = default!;
    public string Content { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateTime? FollowUpDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private Prescription() { }

    public Prescription(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        Guid? treatmentId,
        DateTime prescribedAtUtc,
        string title,
        string content,
        string? notes,
        DateTime? followUpDateUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is invalid.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId is invalid.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId is invalid.", nameof(petId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        TreatmentId = treatmentId;
        PrescribedAtUtc = NormalizeUtc(prescribedAtUtc);
        Title = title.Trim();
        Content = content.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        FollowUpDateUtc = followUpDateUtc.HasValue ? NormalizeUtc(followUpDateUtc.Value) : null;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    public Result UpdateDetails(
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        Guid? treatmentId,
        DateTime prescribedAtUtc,
        string title,
        string content,
        string? notes,
        DateTime? followUpDateUtc)
    {
        if (clinicId == Guid.Empty)
            return Result.Failure("Prescriptions.Validation", "ClinicId is invalid.");
        if (petId == Guid.Empty)
            return Result.Failure("Prescriptions.Validation", "PetId is invalid.");
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure("Prescriptions.Validation", "Title is required.");
        if (string.IsNullOrWhiteSpace(content))
            return Result.Failure("Prescriptions.Validation", "Content is required.");

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        TreatmentId = treatmentId;
        PrescribedAtUtc = NormalizeUtc(prescribedAtUtc);
        Title = title.Trim();
        Content = content.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        FollowUpDateUtc = followUpDateUtc.HasValue ? NormalizeUtc(followUpDateUtc.Value) : null;
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
