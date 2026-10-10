using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Appointments;

/// <summary>
/// Klinik ve hayvana bağlı randevu kaydı.
/// </summary>
public sealed class Appointment : AggregateRoot
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 240;
    public const int DefaultDurationMinutes = 30;
    public const int MinNoShowRevertReasonLength = 5;
    public const int MaxNoShowReasonLength = 500;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public DateTime ScheduledAtUtc { get; private set; }
    /// <summary>Randevu süresi (dakika). Kalıcı alan; bitiş <see cref="ScheduledEndUtc"/> ile türetilir.</summary>
    public int DurationMinutes { get; private set; }
    public AppointmentType AppointmentType { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>
    /// Başarılı domain mutasyon sayacı; optimistic concurrency token.
    /// Yeni kayıt 0 ile başlar; ilk başarılı mutasyon 1 üretir.
    /// </summary>
    public long MutationSequence { get; private set; }

    /// <summary>UTC bitiş zamanı; <see cref="ScheduledAtUtc"/> + <see cref="DurationMinutes"/>.</summary>
    public DateTime ScheduledEndUtc => ScheduledAtUtc.AddMinutes(DurationMinutes);

    private Appointment() { }

    public static bool IsValidDurationMinutes(int value)
        => value >= MinDurationMinutes && value <= MaxDurationMinutes;

    /// <summary>Yeni randevu; <paramref name="initialStatus"/> verilmezse <see cref="AppointmentStatus.Scheduled"/>.</summary>
    public Appointment(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        DateTime scheduledAtUtc,
        int durationMinutes = DefaultDurationMinutes,
        AppointmentType appointmentType = AppointmentType.Other,
        AppointmentStatus? initialStatus = null,
        string? notes = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId geçersiz.", nameof(petId));
        if (!IsValidDurationMinutes(durationMinutes))
            throw new ArgumentOutOfRangeException(nameof(durationMinutes), "Randevu süresi 5-240 dakika arasında olmalıdır.");
        if (!Enum.IsDefined(appointmentType))
            throw new ArgumentOutOfRangeException(nameof(appointmentType));

        var status = initialStatus ?? AppointmentStatus.Scheduled;
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(initialStatus));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ScheduledAtUtc = NormalizeUtc(scheduledAtUtc);
        DurationMinutes = durationMinutes;
        AppointmentType = appointmentType;
        Status = status;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    /// <summary>Başarılı domain mutasyonundan sonra tam bir kez çağrılır.</summary>
    public long AdvanceMutationSequence()
    {
        checked
        {
            MutationSequence++;
        }

        return MutationSequence;
    }

    /// <summary>Yalnızca <see cref="AppointmentStatus.Scheduled"/> iken iptal.</summary>
    public Result Cancel(string? cancellationReason = null)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca planlanmış randevu iptal edilebilir.");
        }

        Status = AppointmentStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(cancellationReason))
        {
            var line = $"İptal: {cancellationReason.Trim()}";
            Notes = string.IsNullOrWhiteSpace(Notes) ? line : $"{Notes}\n{line}";
        }

        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>
    /// Randevulu hasta gelmedi: yalnızca saati geçmiş <see cref="AppointmentStatus.Scheduled"/> randevu.
    /// Zaten <see cref="AppointmentStatus.NoShow"/> ise değişiklik olmadan başarı (idempotent).
    /// </summary>
    public Result MarkNoShow(DateTime nowUtc, string? reason = null)
    {
        if (Status == AppointmentStatus.NoShow)
            return Result.Success();

        if (Status != AppointmentStatus.Scheduled)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca planlanmış randevu gelmedi olarak işaretlenebilir.");
        }

        if (ScheduledAtUtc > NormalizeUtc(nowUtc))
        {
            return Result.Failure(
                "Appointments.NoShowNotYetDue",
                "Randevu saati henüz gelmedi; gelecek randevu gelmedi olarak işaretlenemez.");
        }

        Status = AppointmentStatus.NoShow;
        AppendNote("Gelmedi", reason);
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>Gelmedi işaretini gerekçeyle geri alır (<c>NoShow → Scheduled</c>). Gerekçe zorunludur.</summary>
    public Result RevertNoShow(string? reason)
    {
        if (Status != AppointmentStatus.NoShow)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca gelmedi işaretli randevu geri alınabilir.");
        }

        var length = reason?.Trim().Length ?? 0;
        if (length < MinNoShowRevertReasonLength || length > MaxNoShowReasonLength)
        {
            return Result.Failure(
                "Appointments.Validation",
                $"Gerekçe {MinNoShowRevertReasonLength}-{MaxNoShowReasonLength} karakter olmalıdır.");
        }

        Status = AppointmentStatus.Scheduled;
        AppendNote("Gelmedi geri alındı", reason);
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>Hasta gelmedi işaretinden sonra geldi: işaret gerekçesiz kaldırılır; iz geliş (Visit) kaydıdır.</summary>
    public Result RevertNoShowOnArrival()
    {
        if (Status != AppointmentStatus.NoShow)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca gelmedi işaretli randevu için geçerlidir.");
        }

        Status = AppointmentStatus.Scheduled;
        AppendNote("Gelmedi kaldırıldı", "hasta geç geldi");
        AdvanceMutationSequence();
        return Result.Success();
    }

    private void AppendNote(string label, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var line = $"{label}: {text.Trim()}";
        Notes = string.IsNullOrWhiteSpace(Notes) ? line : $"{Notes}\n{line}";
    }

    /// <summary>Yalnızca <see cref="AppointmentStatus.Scheduled"/> iken tamamlandı.</summary>
    public Result Complete()
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca planlanmış randevu tamamlanabilir.");
        }

        Status = AppointmentStatus.Completed;
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>Yalnızca <see cref="AppointmentStatus.Scheduled"/> iken yeni zaman (uygulama katmanı çakışma kontrolü yapar).</summary>
    public Result RescheduleTo(DateTime scheduledAtUtc)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca planlanmış randevu yeniden zamanlanabilir.");
        }

        ScheduledAtUtc = NormalizeUtc(scheduledAtUtc);
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>
    /// Yalnızca <see cref="AppointmentStatus.Scheduled"/> durumunda randevu detaylarını günceller.
    /// </summary>
    public Result UpdateDetails(
        Guid clinicId,
        Guid petId,
        DateTime scheduledAtUtc,
        int durationMinutes,
        AppointmentType appointmentType,
        string? notes)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Yalnızca planlanmış randevu güncellenebilir.");
        }

        if (clinicId == Guid.Empty)
            return Result.Failure("Appointments.Validation", "ClinicId geçersiz.");
        if (petId == Guid.Empty)
            return Result.Failure("Appointments.Validation", "PetId geçersiz.");
        if (!IsValidDurationMinutes(durationMinutes))
            return Result.Failure(
                "Appointments.Validation",
                "Randevu süresi 5-240 dakika arasında olmalıdır.");
        if (!Enum.IsDefined(appointmentType))
            return Result.Failure("Appointments.Validation", "Randevu türü geçersiz.");

        ClinicId = clinicId;
        PetId = petId;
        ScheduledAtUtc = NormalizeUtc(scheduledAtUtc);
        DurationMinutes = durationMinutes;
        AppointmentType = appointmentType;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        AdvanceMutationSequence();
        return Result.Success();
    }

    /// <summary>
    /// Update/Write akışında istenen durumun mevcut durumla aynı olup olmadığını
    /// scheduling/working-hours doğrulamalarından <em>önce</em> kontrol etmek için ön-kontrol.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Enum tanımsızsa <c>Appointments.Validation</c>.</description></item>
    /// <item><description>İstenen durum mevcut durumdan farklıysa <c>Appointments.InvalidStatusTransition</c>:
    /// durum geçişleri yalnızca kendi izinli uçlarıyla (iptal, tamamlama, gelmedi) yapılır; Update/Create ile yapılamaz.</description></item>
    /// <item><description>Aksi halde başarı; gerçek mutasyon <see cref="ApplyWriteUpdate"/> içinde yapılır.</description></item>
    /// </list>
    /// </remarks>
    public Result EnsureCanApplyStatus(AppointmentStatus requestedStatus)
    {
        if (!Enum.IsDefined(requestedStatus))
            return Result.Failure("Appointments.Validation", "Randevu durumu geçersiz.");

        if (requestedStatus != Status)
        {
            return Result.Failure(
                "Appointments.InvalidStatusTransition",
                "Randevu durumu bu işlemle değiştirilemez; iptal, tamamlama ve gelmedi için ilgili uçları kullanın.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Create/Update write sözleşmesi: durum + zaman/tür alanları.
    /// Durum değiştirilemez (yalnızca aynı durum kabul edilir); tamamlama, iptal ve gelmedi kendi uçlarıyla yapılır.
    /// Planlanmış olmayan kayıtta değişiklik yapılmadan başarı döner.
    /// </summary>
    public Result ApplyWriteUpdate(
        AppointmentStatus requestedStatus,
        Guid clinicId,
        Guid petId,
        DateTime scheduledAtUtc,
        int durationMinutes,
        AppointmentType appointmentType,
        string? notes)
    {
        if (!Enum.IsDefined(requestedStatus))
            return Result.Failure("Appointments.Validation", "Randevu durumu geçersiz.");

        var statusGuard = EnsureCanApplyStatus(requestedStatus);
        if (!statusGuard.IsSuccess)
            return statusGuard;

        if (Status != AppointmentStatus.Scheduled)
            return Result.Success();

        return UpdateDetails(clinicId, petId, scheduledAtUtc, durationMinutes, appointmentType, notes);
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
