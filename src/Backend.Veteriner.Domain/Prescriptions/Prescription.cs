using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Prescriptions;

/// <summary>
/// Clinical prescription record for a pet; optionally linked to an examination and/or a treatment.
/// </summary>
public sealed class Prescription : AggregateRoot, IClinicScoped
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

    /// <summary>HBS e-Reçete geri kaydı (ADR-036 Dilim 2); klinik içerik değildir, kesinleşmiş muayene kilidine tabi değildir.</summary>
    public HbsEntryStatus HbsStatus { get; private set; } = HbsEntryStatus.NotEntered;

    /// <summary>HBS'ten dönen e-Reçete No.</summary>
    public string? HbsReferenceNumber { get; private set; }
    public string? HbsNotRequiredReason { get; private set; }
    public DateTime? HbsMarkedAtUtc { get; private set; }
    public Guid? HbsMarkedByUserId { get; private set; }

    private readonly List<PrescriptionItem> _items = new();

    /// <summary>Yapılandırılmış reçete satırları; eski (serbest metin) reçetelerde boştur.</summary>
    public IReadOnlyList<PrescriptionItem> Items => _items;

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
        DateTime? followUpDateUtc,
        DateTime utcNow)
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
        CreatedAtUtc = utcNow;
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
        DateTime? followUpDateUtc,
        DateTime utcNow)
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
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Tüm satırları verilen liste ile değiştirir (boş liste = hepsini siler); sıra dizideki sıradır.</summary>
    public Result ReplaceItems(IReadOnlyList<PrescriptionItemDraft> drafts)
    {
        if (drafts.Count > PrescriptionItem.MaxPerPrescription)
        {
            return Result.Failure(
                "Prescriptions.Validation",
                $"A prescription can have at most {PrescriptionItem.MaxPerPrescription} items.");
        }

        foreach (var draft in drafts)
        {
            var valid = PrescriptionItem.Validate(draft);
            if (!valid.IsSuccess)
                return valid;
        }

        _items.Clear();
        for (var i = 0; i < drafts.Count; i++)
            _items.Add(new PrescriptionItem(Id, i + 1, drafts[i]));

        return Result.Success();
    }

    /// <summary>HBS geri kaydını yazar (kural <see cref="HbsEntryRules"/>); klinik alanlara ve <see cref="UpdatedAtUtc"/>'e dokunmaz.</summary>
    public Result ApplyHbsEntry(
        HbsEntryStatus status,
        string? referenceNumber,
        string? notRequiredReason,
        Guid markedByUserId,
        DateTime nowUtc)
    {
        var entry = HbsEntryRules.Resolve(
            status, referenceNumber, notRequiredReason, markedByUserId, NormalizeUtc(nowUtc), "Prescriptions.HbsValidation");
        if (!entry.IsSuccess)
            return Result.Failure(entry.Error);

        HbsStatus = entry.Value!.Status;
        HbsReferenceNumber = entry.Value.ReferenceNumber;
        HbsNotRequiredReason = entry.Value.NotRequiredReason;
        HbsMarkedAtUtc = entry.Value.MarkedAtUtc;
        HbsMarkedByUserId = entry.Value.MarkedByUserId;
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
