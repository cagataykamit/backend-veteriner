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
    public AppointmentStatus Status { get; private set; }
    public string? Notes { get; private set; }

    private Appointment() { }

    /// <summary>Yeni randevu her zaman <see cref="AppointmentStatus.Scheduled"/> ile oluşturulur.</summary>
    public Appointment(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        DateTime scheduledAtUtc,
        string? notes = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));
        if (petId == Guid.Empty)
            throw new ArgumentException("PetId geçersiz.", nameof(petId));

        TenantId = tenantId;
        ClinicId = clinicId;
        PetId = petId;
        ScheduledAtUtc = NormalizeUtc(scheduledAtUtc);
        Status = AppointmentStatus.Scheduled;
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
    public Result UpdateDetails(Guid clinicId, Guid petId, DateTime scheduledAtUtc, string? notes)
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

        ClinicId = clinicId;
        PetId = petId;
        ScheduledAtUtc = NormalizeUtc(scheduledAtUtc);
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
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
