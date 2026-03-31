using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Appointments;

/// <summary>
/// Klinik ve hayvana bağlı randevu kaydı.
/// </summary>
public sealed class Appointment : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid PetId { get; private set; }
    public DateTime ScheduledAtUtc { get; private set; }
    public AppointmentType AppointmentType { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string? Notes { get; private set; }

    private Appointment() { }

    /// <summary>Yeni randevu; <paramref name="initialStatus"/> verilmezse <see cref="AppointmentStatus.Scheduled"/>.</summary>
    public Appointment(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        DateTime scheduledAtUtc,
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
        if (!Enum.IsDefined(appointmentType))
            throw new ArgumentOutOfRangeException(nameof(appointmentType));

        var status = initialStatus ?? AppointmentStatus.Scheduled;
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(initialStatus));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ScheduledAtUtc = NormalizeUtc(scheduledAtUtc);
        AppointmentType = appointmentType;
        Status = status;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
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

        return Result.Success();
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
        return Result.Success();
    }

    /// <summary>
    /// Yalnızca <see cref="AppointmentStatus.Scheduled"/> durumunda randevu detaylarını günceller.
    /// </summary>
    public Result UpdateDetails(Guid clinicId, Guid petId, DateTime scheduledAtUtc, AppointmentType appointmentType, string? notes)
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
        if (!Enum.IsDefined(appointmentType))
            return Result.Failure("Appointments.Validation", "Randevu türü geçersiz.");

        ClinicId = clinicId;
        PetId = petId;
        ScheduledAtUtc = NormalizeUtc(scheduledAtUtc);
        AppointmentType = appointmentType;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        return Result.Success();
    }

    /// <summary>
    /// Create/Update write sözleşmesi: durum + zaman/tür alanları.
    /// Tamamlanmış veya iptal edilmiş kayıtta yalnızca aynı durum (değişiklik yok) kabul edilir.
    /// </summary>
    public Result ApplyWriteUpdate(
        AppointmentStatus requestedStatus,
        Guid clinicId,
        Guid petId,
        DateTime scheduledAtUtc,
        AppointmentType appointmentType,
        string? notes)
    {
        if (!Enum.IsDefined(requestedStatus))
            return Result.Failure("Appointments.Validation", "Randevu durumu geçersiz.");

        if (Status != AppointmentStatus.Scheduled)
        {
            if (requestedStatus != Status)
            {
                return Result.Failure(
                    "Appointments.InvalidStatusTransition",
                    "Tamamlanmış veya iptal edilmiş randevunun durumu değiştirilemez.");
            }

            return Result.Success();
        }

        if (requestedStatus == AppointmentStatus.Scheduled)
            return UpdateDetails(clinicId, petId, scheduledAtUtc, appointmentType, notes);

        var details = UpdateDetails(clinicId, petId, scheduledAtUtc, appointmentType, notes);
        if (!details.IsSuccess)
            return details;

        if (requestedStatus == AppointmentStatus.Completed)
            return Complete();

        if (requestedStatus == AppointmentStatus.Cancelled)
            return Cancel(null);

        return Result.Failure("Appointments.Validation", "Randevu durumu geçersiz.");
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}
