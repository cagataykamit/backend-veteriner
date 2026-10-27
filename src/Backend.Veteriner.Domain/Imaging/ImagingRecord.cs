using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Imaging;

/// <summary>
/// Hastaya bağlı görüntüleme kaydı (ADR-007, ASAMA6 I1): tarih, tür, açıklama, klinik not ve dosyalar
/// (<c>StoredFile</c>, sahip <c>ImagingRecord</c>). İsteğe bağlı olarak muayeneye bağlanır. Silinmez, gerekçeyle iptal edilir.
/// </summary>
public sealed class ImagingRecord : AggregateRoot, ITenantOwned
{
    public const int DescriptionMaxLength = 1000;
    public const int ClinicalNoteMaxLength = 4000;

    /// <summary>Saat farkı toleransı: tarih en çok bu kadar ileride olabilir.</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(1);

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? ExaminationId { get; private set; }
    public DateTime PerformedAtUtc { get; private set; }
    public ImagingModality Modality { get; private set; }
    public string? Description { get; private set; }
    public string? ClinicalNote { get; private set; }
    public ImagingRecordStatus Status { get; private set; }

    /// <summary>Kesinleşmiş muayeneye sonradan bağlanmış ek kayıt (ADR-013 uyumu; rozet).</summary>
    public bool AddedAfterExaminationFinalization { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    private ImagingRecord() { }

    private ImagingRecord(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        DateTime performedAtUtc,
        ImagingModality modality,
        string? description,
        string? clinicalNote,
        bool addedAfterExaminationFinalization,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ExaminationId = examinationId;
        PerformedAtUtc = performedAtUtc;
        Modality = modality;
        Description = Trimmed(description);
        ClinicalNote = Trimmed(clinicalNote);
        Status = ImagingRecordStatus.Active;
        AddedAfterExaminationFinalization = addedAfterExaminationFinalization;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = nowUtc;
    }

    /// <param name="examinationFinalized">Bağlanan muayene kesinleşmiş mi (ek kayıt rozeti).</param>
    public static Result<ImagingRecord> Create(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        bool examinationFinalized,
        DateTime performedAtUtc,
        ImagingModality modality,
        string? description,
        string? clinicalNote,
        Guid createdByUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty || petId == Guid.Empty)
            return Result<ImagingRecord>.Failure("ImagingRecords.Validation", "Kiracı, klinik ve hasta zorunludur.");
        if (createdByUserId == Guid.Empty)
            return Result<ImagingRecord>.Failure("ImagingRecords.Validation", "Kaydı oluşturan kullanıcı geçersiz.");

        var normalizedDate = NormalizeUtc(performedAtUtc);
        var check = ValidateDetails(normalizedDate, modality, description, clinicalNote, nowUtc);
        if (!check.IsSuccess)
            return Result<ImagingRecord>.Failure(check.Error);

        return Result<ImagingRecord>.Success(new ImagingRecord(
            tenantId,
            clinicId,
            petId,
            examinationId,
            normalizedDate,
            modality,
            description,
            clinicalNote,
            examinationId.HasValue && examinationFinalized,
            createdByUserId,
            nowUtc));
    }

    /// <summary>Tür, tarih, metinler ve muayene bağı. Hasta ve klinik değişmez (yanlış hasta: iptal + yeni kayıt).</summary>
    public Result UpdateDetails(
        Guid? examinationId,
        DateTime performedAtUtc,
        ImagingModality modality,
        string? description,
        string? clinicalNote,
        DateTime nowUtc)
    {
        if (Status == ImagingRecordStatus.Cancelled)
            return AlreadyCancelled();

        var normalizedDate = NormalizeUtc(performedAtUtc);
        var check = ValidateDetails(normalizedDate, modality, description, clinicalNote, nowUtc);
        if (!check.IsSuccess)
            return check;

        ExaminationId = examinationId;
        PerformedAtUtc = normalizedDate;
        Modality = modality;
        Description = Trimmed(description);
        ClinicalNote = Trimmed(clinicalNote);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Gerekçeli iptal (silmez). Dosyaların erişimini kapatmak çağıranındır (aynı işlemde).</summary>
    public Result Cancel(Guid cancelledByUserId, string? reason, DateTime nowUtc)
    {
        if (Status == ImagingRecordStatus.Cancelled)
            return AlreadyCancelled();
        if (cancelledByUserId == Guid.Empty)
            return Result.Failure("ImagingRecords.Validation", "İptal eden kullanıcı geçersiz.");

        var reasonCheck = CorrectionReason.Validate(reason, "ImagingRecords.Validation");
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        Status = ImagingRecordStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelledByUserId = cancelledByUserId;
        CancelReason = reason!.Trim();
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Kesinleşme kilidi (ADR-013 uyumu, tek kural): bağlı muayene kesinleştiğinde zaten var olan kayıt o muayenenin parçasıdır;
    /// düzenlenemez ve dosya almaz (ek görüntü yeni kayıt olarak girilir). Kesinleşmeden sonra oluşturulan ek kayıt serbesttir.
    /// </summary>
    public bool IsFrozenBy(DateTime? examinationFinalizedAtUtc)
        => examinationFinalizedAtUtc is { } finalizedAt && CreatedAtUtc <= finalizedAt;

    /// <summary>Dosya eklenebilir mi: iptal edilmemiş olmalı; kesinleşme kilidi çağıranın <see cref="IsFrozenBy"/> denetimidir.</summary>
    public Result EnsureAcceptsFiles()
        => Status == ImagingRecordStatus.Cancelled ? AlreadyCancelled() : Result.Success();

    /// <summary>İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.</summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private static Result ValidateDetails(
        DateTime performedAtUtc,
        ImagingModality modality,
        string? description,
        string? clinicalNote,
        DateTime nowUtc)
    {
        if (!Enum.IsDefined(modality))
            return Result.Failure("ImagingRecords.Validation", "Görüntüleme türü geçersiz.");
        if (performedAtUtc == default)
            return Result.Failure("ImagingRecords.Validation", "Görüntüleme tarihi zorunludur.");
        if (performedAtUtc > nowUtc + FutureTolerance)
            return Result.Failure("ImagingRecords.Validation", "Görüntüleme tarihi gelecekte olamaz.");
        if (description?.Trim().Length > DescriptionMaxLength)
            return Result.Failure("ImagingRecords.Validation", $"Açıklama en çok {DescriptionMaxLength} karakter olabilir.");
        if (clinicalNote?.Trim().Length > ClinicalNoteMaxLength)
            return Result.Failure("ImagingRecords.Validation", $"Klinik not en çok {ClinicalNoteMaxLength} karakter olabilir.");
        return Result.Success();
    }

    private static Result AlreadyCancelled()
        => Result.Failure("ImagingRecords.AlreadyCancelled", "Görüntüleme kaydı iptal edilmiş; değiştirilemez.");

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
