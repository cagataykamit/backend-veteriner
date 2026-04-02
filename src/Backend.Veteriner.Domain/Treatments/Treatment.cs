using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Treatments;

/// <summary>
/// Clinical treatment record for a pet; optionally linked to an examination.
/// </summary>
public sealed class Treatment : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public DateTime TreatmentDateUtc { get; private set; }
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string? Notes { get; private set; }
    public DateTime? FollowUpDateUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private Treatment() { }

    public Treatment(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime treatmentDateUtc,
        string title,
        string description,
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
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        TreatmentDateUtc = NormalizeUtc(treatmentDateUtc);
        Title = title.Trim();
        Description = description.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        FollowUpDateUtc = followUpDateUtc.HasValue ? NormalizeUtc(followUpDateUtc.Value) : null;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    public Result UpdateDetails(
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime treatmentDateUtc,
        string title,
        string description,
        string? notes,
        DateTime? followUpDateUtc)
    {
        if (clinicId == Guid.Empty)
            return Result.Failure("Treatments.Validation", "ClinicId is invalid.");
        if (petId == Guid.Empty)
            return Result.Failure("Treatments.Validation", "PetId is invalid.");
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure("Treatments.Validation", "Title is required.");
        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure("Treatments.Validation", "Description is required.");

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        TreatmentDateUtc = NormalizeUtc(treatmentDateUtc);
        Title = title.Trim();
        Description = description.Trim();
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
