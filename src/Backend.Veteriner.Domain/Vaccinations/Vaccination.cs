using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Vaccinations;

/// <summary>
/// Pet bazlı aşı kaydı; opsiyonel olarak bir muayene ile ilişkilendirilebilir.
/// Aşı adı <see cref="VaccineName"/> alanında seçilen <c>VaccineDefinition</c> adının snapshot'ı olarak tutulur.
/// </summary>
public sealed class Vaccination : AggregateRoot, IClinicScoped, ITenantOwned, IChangeTracked
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

    /// <summary>
    /// <see cref="DueAtUtc"/>'nin saati gerçek mi (takvimde saatiyle durur); değilse yalnız gün bilinir. Vadesiz kayıtta her zaman false.
    /// Vade ve gecikme kuralı gün bazlıdır (ADR-028); saat yalnız sıralama ve gösterim içindir.
    /// </summary>
    public bool DueTimeSpecified { get; private set; }

    public VaccinationStatus Status { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Uygulama yolu ve bölgesi (RECORD-005); aşıya bağlı stok uygulaması bunları ayrıca taşımaz.</summary>
    public AdministrationRoute? Route { get; private set; }
    public string? Site { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }

    /// <summary>
    /// Gerekçeli iptal (ADR-043 AK-3): ayrı eylemdir, son durumdur. Bağlı uygulamanın iptaliyle iptal edilen aşıda boştur
    /// (gerekçe uygulama kaydındadır).
    /// </summary>
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelReason { get; private set; }
    public bool IsCancelled => Status == VaccinationStatus.Cancelled;

    /// <summary>
    /// Bağlı muayene kayıt anında kesinleşmişse true (ADR-043 AK-6, ADR-013 Ek 2); gerekçe <see cref="LateEntryReason"/>.
    /// Ürünlü ve ürünsüz aşıda aynı kural.
    /// </summary>
    public bool AddedAfterFinalization { get; private set; }
    public string? LateEntryReason { get; private set; }

    public const string LateEntryReasonRequiredCode = "Vaccinations.LateEntryReasonRequired";

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

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
        DateTime utcNow,
        bool dueTimeSpecified = false)
    {
        if (importBatchId == Guid.Empty)
            throw new ArgumentException("ImportBatchId geçersiz.", nameof(importBatchId));

        return new Vaccination(
            tenantId, petId, clinicId, examinationId: null, vaccineDefinitionId, vaccineNameSnapshot,
            VaccinationStatus.Applied, appliedAtUtc, dueAtUtc, notes, utcNow)
        {
            ImportBatchId = importBatchId,
            DueTimeSpecified = dueAtUtc.HasValue && dueTimeSpecified,
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
            return Result.Failure("Vaccinations.Validation", "Geçerli bir hasta seçin.");
        if (clinicId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "Geçerli bir klinik seçin.");
        if (vaccineDefinitionId == Guid.Empty)
            return Result.Failure("Vaccinations.Validation", "Geçerli bir aşı seçin.");
        if (string.IsNullOrWhiteSpace(vaccineNameSnapshot))
            return Result.Failure("Vaccinations.Validation", "Aşı adı zorunludur.");

        var transition = EnsureStatusTransition(Status, status);
        if (!transition.IsSuccess)
            return transition;

        var subject = RecordSubjectRule.EnsureUnchanged(ClinicId, PetId, clinicId, petId, "Vaccinations");
        if (!subject.IsSuccess)
            return subject;

        var newDueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : (DateTime?)null;

        PetId = petId;
        ClinicId = clinicId;
        ExaminationId = examinationId;
        VaccineDefinitionId = vaccineDefinitionId;
        VaccineName = vaccineNameSnapshot.Trim();
        Status = status;
        AppliedAtUtc = appliedAtUtc.HasValue ? NormalizeUtc(appliedAtUtc.Value) : null;
        // Vade değişmezse saatin gerçekliği korunur; değişen vade panelden gelir ve saati gerçek sayılmaz.
        DueTimeSpecified = DueTimeSpecified && newDueAtUtc is not null && newDueAtUtc == DueAtUtc;
        DueAtUtc = newDueAtUtc;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Uygulama yolu ve bölgesini yazar (oluşturma ve düzenlemede; ikisi de tam değiştirme, <c>null</c> temizler).</summary>
    public Result SetRouteAndSite(AdministrationRoute? route, string? site)
    {
        var normalized = AdministrationRouteSite.Normalize(route, site, "Vaccinations.Validation");
        if (!normalized.IsSuccess)
            return Result.Failure(normalized.Error);

        Route = normalized.Value.Route;
        Site = normalized.Value.Site;
        return Result.Success();
    }

    /// <summary>
    /// Durum geçişi kuralı (tek yer, ADR-043 AK-3): güncellemede izinli tek geçiş Planlı → Uygulandı. İptal ayrı eylemdir
    /// (<see cref="Cancel"/>) ve son durumdur; uygulanmış aşı planlıya dönmez.
    /// </summary>
    public static Result EnsureStatusTransition(VaccinationStatus current, VaccinationStatus next)
    {
        if (current == VaccinationStatus.Cancelled)
            return CancelledFailure();
        if (next == current || (current == VaccinationStatus.Scheduled && next == VaccinationStatus.Applied))
            return Result.Success();
        if (next == VaccinationStatus.Cancelled)
            return Result.Failure(InvalidStatusTransitionCode, "Aşı kaydı güncellemeyle iptal edilemez; iptal için gerekçeyle 'İptal et' kullanılır.");

        return Result.Failure(InvalidStatusTransitionCode, "Uygulanmış aşı planlı duruma döndürülemez; yanlış kayıt gerekçeyle iptal edilir.");
    }

    public const string InvalidStatusTransitionCode = "Vaccinations.InvalidStatusTransition";

    /// <summary>
    /// Aşı kaydını gerekçeyle iptal eder (silmez; kural <see cref="RecordCancellationRules"/>). Bağlı uygulama (stok düşümü)
    /// varsa iptal uygulama iptaliyle yapılır; bu denetim çağıranındır. İptal kayıtta uygulama tarihi olmaz (durum/tarih kuralı);
    /// önceki değer değişiklik geçmişinde kalır.
    /// </summary>
    public Result Cancel(Guid cancelledByUserId, string? reason, DateTime utcNow)
    {
        if (IsCancelled)
            return Result.Failure("Vaccinations.AlreadyCancelled", "Aşı kaydı zaten iptal edilmiş.");

        var cancellation = RecordCancellationRules.Resolve(cancelledByUserId, reason, utcNow, "Vaccinations.Validation");
        if (!cancellation.IsSuccess)
            return Result.Failure(cancellation.Error);

        Status = VaccinationStatus.Cancelled;
        AppliedAtUtc = null;
        CancelledAtUtc = cancellation.Value!.CancelledAtUtc;
        CancelledByUserId = cancellation.Value.CancelledByUserId;
        CancelReason = cancellation.Value.Reason;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Kesinleşmiş muayeneye sonradan eklenen aşıyı işaretler; gerekçe zorunlu (5–500). Yalnız oluşturmada çağrılır.</summary>
    public Result MarkAddedAfterFinalization(string? reason)
    {
        var check = CorrectionReason.Validate(reason, LateEntryReasonRequiredCode);
        if (!check.IsSuccess)
            return check;

        AddedAfterFinalization = true;
        LateEntryReason = reason!.Trim();
        return Result.Success();
    }

    /// <summary>İstemcinin gördüğü sürüm: kalıcı yazma yalnızca veritabanındaki sürüm bu değere eşitse gerçekleşir.</summary>
    public void SetExpectedRowVersion(byte[] expectedRowVersion)
    {
        if (expectedRowVersion is null || expectedRowVersion.Length == 0)
            throw new ArgumentException("RowVersion geçersiz.", nameof(expectedRowVersion));

        RowVersion = expectedRowVersion;
    }

    private static Result CancelledFailure()
        => Result.Failure("Vaccinations.AlreadyCancelled", "İptal edilmiş aşı kaydı değiştirilemez.");

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
