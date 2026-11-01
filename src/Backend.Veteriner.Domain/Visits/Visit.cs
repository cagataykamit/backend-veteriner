using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Visits;

/// <summary>
/// Hastanın klinikteki gerçek gelişi. Randevulu (<see cref="AppointmentId"/> dolu) veya randevusuz olabilir;
/// randevusuz geliş takvime randevu yazmaz. Randevu planı bu varlıktan bağımsızdır.
/// Yanlış geliş silinmez, <see cref="VoidedAtUtc"/> ile işaretlenir ve kuyruktan düşer.
/// </summary>
public sealed class Visit : AggregateRoot, IClinicScoped, ITenantOwned
{
    public const int MinCorrectionReasonLength = 5;
    public const int MaxCorrectionReasonLength = 500;
    public const int MaxArrivalReasonLength = 500;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? ResponsibleVeterinarianUserId { get; private set; }
    public DateTime ArrivedAtUtc { get; private set; }
    public VisitCareStatus CareStatus { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsUrgent { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Geliş hazırlığı (CHECKIN-001/002): resepsiyonun gelişte yazdığı şikâyet / geliş nedeni. Muayene açılırken geliş nedeni alanına
    /// önerilir; muayene kaydına sunucuda kopyalanmaz.
    /// </summary>
    public string? ArrivalReason { get; private set; }

    /// <summary>
    /// Geliş hazırlığı (CHECKIN-002/003): gelişte seçilen muayene şablonu veya tedavi paketi (EXAM-006/007). Uygulanmaz; hekim muayenede
    /// mevcut "şablon uygula" akışıyla onaylar (stok ve ücret kalemleri o anda doğar).
    /// </summary>
    public Guid? PreparedExaminationTemplateId { get; private set; }

    /// <summary>
    /// Başarılı domain mutasyon sayacı; optimistic concurrency token ve projeksiyon sıralama anahtarı.
    /// Yeni kayıt 0 ile başlar; her başarılı mutasyon 1 artırır.
    /// </summary>
    public long MutationSequence { get; private set; }

    public bool IsVoided => VoidedAtUtc.HasValue;

    private Visit() { }

    public Visit(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? appointmentId,
        Guid? responsibleVeterinarianUserId,
        Guid createdByUserId,
        DateTime arrivedAtUtc,
        bool isUrgent = false,
        string? arrivalReason = null,
        Guid? preparedExaminationTemplateId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId geçersiz.", nameof(petId));
        if (appointmentId == Guid.Empty)
            throw new ArgumentException("AppointmentId geçersiz.", nameof(appointmentId));
        if (responsibleVeterinarianUserId == Guid.Empty)
            throw new ArgumentException("Sorumlu hekim geçersiz.", nameof(responsibleVeterinarianUserId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("Oluşturan kullanıcı geçersiz.", nameof(createdByUserId));

        var preparation = ValidatePreparation(arrivalReason, preparedExaminationTemplateId);
        if (!preparation.IsSuccess)
            throw new ArgumentException(preparation.Error.Message, nameof(arrivalReason));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        AppointmentId = appointmentId;
        ResponsibleVeterinarianUserId = responsibleVeterinarianUserId;
        CreatedByUserId = createdByUserId;
        ArrivedAtUtc = NormalizeUtc(arrivedAtUtc);
        CreatedAtUtc = ArrivedAtUtc;
        CareStatus = VisitCareStatus.Waiting;
        IsUrgent = isUrgent;
        ArrivalReason = NormalizeArrivalReason(arrivalReason);
        PreparedExaminationTemplateId = preparedExaminationTemplateId;
    }

    /// <summary>
    /// Geliş hazırlığını değiştirir (tam yazım: null alan temizler). Tamamlanmış gelişte değiştirilemez; aynı değerler idempotenttir.
    /// Şablonun kiracıya görünür ve aktif olduğu uygulama katmanında denetlenir.
    /// </summary>
    public Result SetPreparation(string? arrivalReason, Guid? preparedExaminationTemplateId)
    {
        if (IsVoided)
            return VoidedFailure();

        var check = ValidatePreparation(arrivalReason, preparedExaminationTemplateId);
        if (!check.IsSuccess)
            return check;

        var reason = NormalizeArrivalReason(arrivalReason);
        if (reason == ArrivalReason && preparedExaminationTemplateId == PreparedExaminationTemplateId)
            return Result.Success();

        if (CareStatus == VisitCareStatus.Completed)
        {
            return Result.Failure(
                "Visits.NotOpen",
                "Tamamlanmış gelişin hazırlığı değiştirilemez.");
        }

        ArrivalReason = reason;
        PreparedExaminationTemplateId = preparedExaminationTemplateId;
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>Geliş hazırlığı için tek doğrulama kuralı (domain ve validator paylaşır).</summary>
    public static Result ValidatePreparation(string? arrivalReason, Guid? preparedExaminationTemplateId)
    {
        if ((arrivalReason?.Trim().Length ?? 0) > MaxArrivalReasonLength)
            return Result.Failure("Visits.Validation", $"Geliş nedeni en fazla {MaxArrivalReasonLength} karakter olabilir.");

        if (preparedExaminationTemplateId == Guid.Empty)
            return Result.Failure("Visits.Validation", "Şablon kimliği geçersiz.");

        return Result.Success();
    }

    private static string? NormalizeArrivalReason(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Bekliyor → Devam ediyor. Zaten devam ediyorsa değişiklik olmadan başarı (idempotent).</summary>
    public Result Start(DateTime nowUtc)
    {
        if (IsVoided)
            return VoidedFailure();

        if (CareStatus == VisitCareStatus.InProgress)
            return Result.Success();

        if (CareStatus != VisitCareStatus.Waiting)
        {
            return Result.Failure(
                "Visits.InvalidStatusTransition",
                "Yalnızca bekleyen geliş başlatılabilir.");
        }

        CareStatus = VisitCareStatus.InProgress;
        StartedAtUtc = NormalizeUtc(nowUtc);
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>Devam ediyor → Tamamlandı. Zaten tamamlandıysa değişiklik olmadan başarı (idempotent).</summary>
    public Result Complete(DateTime nowUtc)
    {
        if (IsVoided)
            return VoidedFailure();

        if (CareStatus == VisitCareStatus.Completed)
            return Result.Success();

        if (CareStatus != VisitCareStatus.InProgress)
        {
            return Result.Failure(
                "Visits.InvalidStatusTransition",
                "Yalnızca devam eden geliş tamamlanabilir.");
        }

        CareStatus = VisitCareStatus.Completed;
        CompletedAtUtc = NormalizeUtc(nowUtc);
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>
    /// Düzeltme: herhangi bir bakım durumuna geri/ileri geçiş. Gerekçe zorunludur.
    /// Aynı hayvanda başka aktif geliş olup olmadığı uygulama katmanında kontrol edilir.
    /// </summary>
    public Result CorrectCareStatus(VisitCareStatus target, string? reason, DateTime nowUtc)
    {
        if (IsVoided)
            return VoidedFailure();

        var reasonCheck = ValidateReason(reason);
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        if (!Enum.IsDefined(target))
            return Result.Failure("Visits.Validation", "Hedef bakım durumu geçersiz.");

        if (target == CareStatus)
            return Result.Failure("Visits.Validation", "Hedef durum mevcut durumla aynı.");

        var now = NormalizeUtc(nowUtc);
        switch (target)
        {
            case VisitCareStatus.Waiting:
                StartedAtUtc = null;
                CompletedAtUtc = null;
                break;
            case VisitCareStatus.InProgress:
                StartedAtUtc ??= now;
                CompletedAtUtc = null;
                break;
            case VisitCareStatus.Completed:
                StartedAtUtc ??= now;
                CompletedAtUtc = now;
                break;
        }

        CareStatus = target;
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>Yanlış geliş işareti: kayıt korunur, kuyruktan ve benzersiz kurallardan çıkar. Gerekçe zorunludur.</summary>
    public Result MarkAsMistaken(string? reason, DateTime nowUtc)
    {
        if (IsVoided)
            return VoidedFailure();

        var reasonCheck = ValidateReason(reason);
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        VoidedAtUtc = NormalizeUtc(nowUtc);
        VoidReason = reason!.Trim();
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>
    /// Yanlış geliş işaretini geri alır; bakım durumu ve zaman damgaları korunur. Gerekçe zorunludur.
    /// Aynı hayvan/randevu için başka geliş olup olmadığı uygulama katmanında kontrol edilir.
    /// </summary>
    public Result RestoreFromMistaken(string? reason)
    {
        if (!IsVoided)
            return Result.Failure("Visits.Validation", "Geliş kaydı yanlış geliş olarak işaretli değil.");

        var reasonCheck = ValidateReason(reason);
        if (!reasonCheck.IsSuccess)
            return reasonCheck;

        VoidedAtUtc = null;
        VoidReason = null;
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>
    /// Acil işareti (basit öncelik bayrağı, triage skoru yok). Yalnızca açık geliş değiştirilir; aynı değer idempotent başarıdır.
    /// </summary>
    public Result SetUrgent(bool isUrgent)
    {
        if (IsVoided)
            return VoidedFailure();

        if (IsUrgent == isUrgent)
            return Result.Success();

        if (CareStatus == VisitCareStatus.Completed)
        {
            return Result.Failure(
                "Visits.NotOpen",
                "Tamamlanmış gelişin acil işareti değiştirilemez.");
        }

        IsUrgent = isUrgent;
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>Düzeltme gerekçesi için tek doğrulama kuralı (domain ve validator paylaşır).</summary>
    public static Result ValidateReason(string? reason)
    {
        var length = reason?.Trim().Length ?? 0;
        if (length < MinCorrectionReasonLength || length > MaxCorrectionReasonLength)
        {
            return Result.Failure(
                "Visits.Validation",
                $"Gerekçe {MinCorrectionReasonLength}-{MaxCorrectionReasonLength} karakter olmalıdır.");
        }

        return Result.Success();
    }

    private long AdvanceMutationSequence()
    {
        checked
        {
            MutationSequence++;
        }

        return MutationSequence;
    }

    private static Result VoidedFailure()
        => Result.Failure("Visits.NotFound", "Geliş kaydı bulunamadı.");

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
