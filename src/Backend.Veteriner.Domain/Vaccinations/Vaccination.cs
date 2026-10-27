using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Vaccinations;

/// <summary>
/// Pet bazlı aşı kaydı; opsiyonel olarak bir muayene ile ilişkilendirilebilir.
/// Aşı adı <see cref="VaccineName"/> alanında seçilen <c>VaccineDefinition</c> adının snapshot'ı olarak tutulur.
/// </summary>
public sealed class Vaccination : AggregateRoot, IClinicScoped
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid? ExaminationId { get; private set; }

    /// <summary>Katalog tanımı kimliği; yeni kayıtlarda dolu olmalı.</summary>
    public Guid? VaccineDefinitionId { get; private set; }

    /// <summary>Seçilen tanım adının uygulama anı snapshot'ı.</summary>
    public string VaccineName { get; private set; } = default!;

    public DateTime? AppliedAtUtc { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public VaccinationStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Kaydı oluşturan içe aktarma partisi; günlük girişte boştur. Partinin hatırlatmaları serbest bırakılana kadar
    /// kayıt hatırlatma adaylarından dışlanır (ADR-019 K3).
    /// </summary>
    public Guid? ImportBatchId { get; private set; }

    /// <summary>
    /// HBS ATS aşı kaydı geri kaydı (ADR-036 Dilim 2). ATS bir aşı olayını kaydettiği için aşı kaydında tutulur (uygulama yalnız stok düşümüdür);
    /// klinik içerik değildir, kesinleşmiş muayene kilidine tabi değildir.
    /// </summary>
    public HbsEntryStatus HbsStatus { get; private set; } = HbsEntryStatus.NotEntered;

    /// <summary>HBS'ten dönen ATS aşı uygulama belgesi seri no.</summary>
    public string? HbsReferenceNumber { get; private set; }
    public string? HbsNotRequiredReason { get; private set; }
    public DateTime? HbsMarkedAtUtc { get; private set; }
    public Guid? HbsMarkedByUserId { get; private set; }

    private Vaccination() { }

    public Vaccination(
        Guid tenantId,
        Guid petId,
        Guid clinicId,
        Guid? examinationId,
        Guid vaccineDefinitionId,
        string vaccineNameSnapshot,
        VaccinationStatus status,
        DateTime? appliedAtUtc,
        DateTime? dueAtUtc,
        string? notes,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId geçersiz.", nameof(petId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (vaccineDefinitionId == Guid.Empty)
            throw new ArgumentException("VaccineDefinitionId geçersiz.", nameof(vaccineDefinitionId));
        if (string.IsNullOrWhiteSpace(vaccineNameSnapshot))
            throw new ArgumentException("Aşı adı snapshot boş olamaz.", nameof(vaccineNameSnapshot));

        TenantId = tenantId;
        PetId = petId;
        ClinicId = clinicId;
        ExaminationId = examinationId;
        VaccineDefinitionId = vaccineDefinitionId;
        VaccineName = vaccineNameSnapshot.Trim();
        Status = status;
        AppliedAtUtc = appliedAtUtc.HasValue ? NormalizeUtc(appliedAtUtc.Value) : null;
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = null;
    }

    /// <summary>
    /// İçe aktarılan geçmiş aşı (ADR-019): her zaman <see cref="VaccinationStatus.Applied"/>, muayenesiz, partiye bağlı.
    /// Stok/uygulama kaydı oluşmaz. Tarih kuralları çağıran içe aktarma yolunda mevcut kural sınıflarıyla denetlenir.
    /// </summary>
    public static Vaccination CreateImported(
        Guid tenantId,
        Guid petId,
        Guid clinicId,
        Guid vaccineDefinitionId,
        string vaccineNameSnapshot,
        DateTime appliedAtUtc,
        DateTime? dueAtUtc,
        string? notes,
        Guid importBatchId,
        DateTime utcNow)
    {
        if (importBatchId == Guid.Empty)
            throw new ArgumentException("ImportBatchId geçersiz.", nameof(importBatchId));

        return new Vaccination(
            tenantId, petId, clinicId, examinationId: null, vaccineDefinitionId, vaccineNameSnapshot,
            VaccinationStatus.Applied, appliedAtUtc, dueAtUtc, notes, utcNow)
        {
            ImportBatchId = importBatchId,
        };
    }

    public Result UpdateDetails(
        Guid petId,
        Guid clinicId,
        Guid? examinationId,
        Guid vaccineDefinitionId,
        string vaccineNameSnapshot,
        VaccinationStatus status,
        DateTime? appliedAtUtc,
        DateTime? dueAtUtc,
        string? notes,
        DateTime utcNow)
    {
        if (petId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "PetId gecersiz.");
        if (clinicId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "ClinicId gecersiz.");
        if (vaccineDefinitionId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "VaccineDefinitionId gecersiz.");
        if (string.IsNullOrWhiteSpace(vaccineNameSnapshot))
            return Result.Failure("Vaccinations.Validation", "Asi adi snapshot bos olamaz.");

        PetId = petId;
        ClinicId = clinicId;
        ExaminationId = examinationId;
        VaccineDefinitionId = vaccineDefinitionId;
        VaccineName = vaccineNameSnapshot.Trim();
        Status = status;
        AppliedAtUtc = appliedAtUtc.HasValue ? NormalizeUtc(appliedAtUtc.Value) : null;
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Bağlı uygulama iptal edildiğinde aşı kaydını iptal eder. İptal kayıtta uygulama tarihi olmaz (durum/tarih kuralı);
    /// gerçek uygulama zamanı iptal edilen <c>Administration</c> kaydında kalır.
    /// </summary>
    public void CancelForVoidedAdministration(DateTime utcNow)
    {
        Status = VaccinationStatus.Cancelled;
        AppliedAtUtc = null;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>
    /// HBS geri kaydını yazar (kural <see cref="HbsEntryRules"/>); yalnız uygulanmış aşıda. Klinik alanlara ve
    /// <see cref="UpdatedAtUtc"/>'e dokunmaz.
    /// </summary>
    public Result ApplyHbsEntry(
        HbsEntryStatus status,
        string? referenceNumber,
        string? notRequiredReason,
        Guid markedByUserId,
        DateTime nowUtc)
    {
        if (Status != VaccinationStatus.Applied)
        {
            return Result.Failure(
                "Vaccinations.HbsNotApplicable",
                "HBS kaydı yalnız uygulanmış aşı için işaretlenebilir.");
        }

        var entry = HbsEntryRules.Resolve(
            status, referenceNumber, notRequiredReason, markedByUserId, NormalizeUtc(nowUtc), "Vaccinations.HbsValidation");
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
