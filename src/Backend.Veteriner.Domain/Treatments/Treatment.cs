using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Treatments;

/// <summary>
/// Clinical treatment record for a pet; optionally linked to an examination.
/// </summary>
public sealed class Treatment : AggregateRoot, IClinicScoped, ITenantOwned
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

    /// <summary>
    /// Gerekçeli iptal (ADR-043 AK-2): tedavi silinmez; iptal edilen tedavi değiştirilemez, kayıt defterinde işaretli kalır.
    /// Kesinleşmiş muayeneye bağlı tedavide de yapılabilir (düzeltme yolu "gerekçeli iptal + yeni kayıt").
    /// </summary>
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public bool IsCancelled => CancelledAtUtc.HasValue;

    /// <summary>
    /// Bağlı muayene kayıt anında kesinleşmişse true (ADR-043 AK-6, ADR-013 Ek 2); gerekçe <see cref="LateEntryReason"/>.
    /// </summary>
    public bool AddedAfterFinalization { get; private set; }
    public string? LateEntryReason { get; private set; }

    public const string LateEntryReasonRequiredCode = "Treatments.LateEntryReasonRequired";

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için (ADR-043 K5).</summary>
    public byte[] RowVersion { get; private set; } = default!;

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
        CreatedAtUtc = utcNow;
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
        DateTime? followUpDateUtc,
        DateTime utcNow)
    {
        if (IsCancelled)
            return CancelledFailure();
        if (clinicId == Guid.Empty)
            return Result.Failure("Treatments.Validation", "Geçerli bir klinik seçin.");
        var subject = RecordSubjectRule.EnsureUnchanged(ClinicId, PetId, clinicId, petId, "Treatments");
        if (!subject.IsSuccess)
            return subject;
        if (petId == Guid.Empty)
            return Result.Failure("Treatments.Validation", "Geçerli bir hasta seçin.");
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure("Treatments.Validation", "Başlık zorunludur.");
        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure("Treatments.Validation", "Açıklama zorunludur.");

        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        TreatmentDateUtc = NormalizeUtc(treatmentDateUtc);
        Title = title.Trim();
        Description = description.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        FollowUpDateUtc = followUpDateUtc.HasValue ? NormalizeUtc(followUpDateUtc.Value) : null;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Tedaviyi gerekçeyle iptal eder (silmez; kural <see cref="RecordCancellationRules"/>). Bağlı etkin reçete ya da uygulama
    /// varsa iptal reddedilir; bu denetim çağıranındır (uygulama iptali stok sonucu ister).
    /// </summary>
    public Result Cancel(Guid cancelledByUserId, string? reason, DateTime utcNow)
    {
        if (IsCancelled)
            return Result.Failure("Treatments.AlreadyCancelled", "Tedavi zaten iptal edilmiş.");

        var cancellation = RecordCancellationRules.Resolve(cancelledByUserId, reason, utcNow, "Treatments.Validation");
        if (!cancellation.IsSuccess)
            return Result.Failure(cancellation.Error);

        CancelledAtUtc = cancellation.Value!.CancelledAtUtc;
        CancelledByUserId = cancellation.Value.CancelledByUserId;
        CancelReason = cancellation.Value.Reason;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Kesinleşmiş muayeneye sonradan eklenen tedaviyi işaretler; gerekçe zorunlu (5–500). Yalnız oluşturmada çağrılır.</summary>
    public Result MarkAddedAfterFinalization(string? reason)
    {
        var check = CorrectionReason.Validate(reason, LateEntryReasonRequiredCode);
        if (!check.IsSuccess)
            return check;

        AddedAfterFinalization = true;
        LateEntryReason = reason!.Trim();
        return Result.Success();
    }

    /// <summary>İptal edilmiş tedaviye yeni kayıt (reçete, uygulama) bağlanamaz; bağlayan işleyiciler bunu çağırır.</summary>
    public Result EnsureAcceptsLinkedRecords()
        => IsCancelled
            ? Result.Failure("Treatments.AlreadyCancelled", "İptal edilmiş tedaviye kayıt bağlanamaz.")
            : Result.Success();

    /// <summary>İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.</summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private static Result CancelledFailure()
        => Result.Failure("Treatments.AlreadyCancelled", "İptal edilmiş tedavi değiştirilemez.");

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
