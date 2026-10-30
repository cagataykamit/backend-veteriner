using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Prescriptions;

/// <summary>
/// Clinical prescription record for a pet; optionally linked to an examination and/or a treatment.
/// </summary>
public sealed class Prescription : AggregateRoot, IClinicScoped, ITenantOwned
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

    /// <summary>İptal (RECORD-008): reçete silinmez; gerekçe ve iptal eden saklanır, iptal edilen reçete değiştirilemez.</summary>
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public bool IsCancelled => CancelledAtUtc.HasValue;

    /// <summary>Bağlı muayene kayıt anında kesinleşmişse true (ADR-013 uyarlaması, RECORD-008); gerekçe <see cref="LateEntryReason"/>.</summary>
    public bool AddedAfterFinalization { get; private set; }
    public string? LateEntryReason { get; private set; }

    public const string LateEntryReasonRequiredCode = "Prescriptions.LateEntryReasonRequired";

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
        if (IsCancelled)
            return CancelledFailure();
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

    /// <summary>
    /// Satır listesini verilen liste yapar (boş liste = hepsini siler); sıra dizideki sıradır. Kimliği verilen satır yerinde
    /// güncellenir (kimlik kararlıdır, RECORD-008), kimliksiz satır yeni eklenir, listede olmayan satır silinir.
    /// </summary>
    public Result ReplaceItems(IReadOnlyList<PrescriptionItemDraft> drafts)
    {
        if (IsCancelled)
            return CancelledFailure();
        if (drafts.Count > PrescriptionItem.MaxPerPrescription)
        {
            return Result.Failure(
                "Prescriptions.Validation",
                $"A prescription can have at most {PrescriptionItem.MaxPerPrescription} items.");
        }

        var ids = drafts.Where(d => d.Id.HasValue).Select(d => d.Id!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count)
            return Result.Failure("Prescriptions.Validation", "An item id can appear only once.");
        if (ids.Any(id => _items.All(i => i.Id != id)))
            return Result.Failure("Prescriptions.Validation", "Item id does not belong to this prescription.");

        foreach (var draft in drafts)
        {
            var valid = PrescriptionItem.Validate(draft);
            if (!valid.IsSuccess)
                return valid;
        }

        _items.RemoveAll(i => !ids.Contains(i.Id));
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            var existing = draft.Id is { } id ? _items.Single(x => x.Id == id) : null;
            if (existing is not null)
                existing.Apply(i + 1, draft);
            else
                _items.Add(new PrescriptionItem(Id, i + 1, draft));
        }

        return Result.Success();
    }

    /// <summary>Kesinleşmiş muayeneye sonradan eklenen reçeteyi işaretler; gerekçe zorunlu (5–500). Yalnız oluşturmada çağrılır.</summary>
    public Result MarkAddedAfterFinalization(string? reason)
    {
        var check = CorrectionReason.Validate(reason, LateEntryReasonRequiredCode);
        if (!check.IsSuccess)
            return check;

        AddedAfterFinalization = true;
        LateEntryReason = reason!.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Reçeteyi gerekçeyle iptal eder (silmez). Kesinleşmiş muayeneye bağlı reçetede de yapılabilir: düzeltme yolu
    /// "gerekçeli iptal + yeni kayıt"tır (ADR-013, Aşama 5 D3).
    /// </summary>
    public Result Cancel(Guid cancelledByUserId, string? reason, DateTime utcNow)
    {
        if (IsCancelled)
            return Result.Failure("Prescriptions.AlreadyCancelled", "Reçete zaten iptal edilmiş.");
        if (cancelledByUserId == Guid.Empty)
            return Result.Failure("Prescriptions.Validation", "İptal eden kullanıcı geçersiz.");

        var check = CorrectionReason.Validate(reason, "Prescriptions.Validation");
        if (!check.IsSuccess)
            return check;

        CancelledAtUtc = NormalizeUtc(utcNow);
        CancelledByUserId = cancelledByUserId;
        CancelReason = reason!.Trim();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    private static Result CancelledFailure()
        => Result.Failure("Prescriptions.AlreadyCancelled", "İptal edilmiş reçete değiştirilemez.");

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
