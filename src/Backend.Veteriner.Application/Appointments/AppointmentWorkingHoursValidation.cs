using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Appointments;

/// <summary>
/// Panelden verilen randevunun çalışma saatine uyumu (kural tek yer: oluşturma, düzenleme, yeniden zamanlama, bekleme listesi teklifi).
/// Klinik çalışma saati kısıtını kapattıysa (<see cref="ClinicAppointmentSettings.EnforceWorkingHours"/>) yalnız tek yerel gün kuralı
/// kalır. Online randevu bu sınıfı değil müsaitlik hesabını kullanır; ayardan etkilenmez.
/// </summary>
internal static class AppointmentWorkingHoursValidation
{
    private static readonly TimeZoneInfo ClinicTimeZone = ResolveClinicTimeZone();

    public static Result Validate(
        DateTime scheduledAtUtc,
        int durationMinutes,
        IReadOnlyList<ClinicWorkingHour> clinicHoursRows,
        ClinicAppointmentSettings? settings)
    {
        var localStart = ToClinicLocal(scheduledAtUtc);
        var localEnd = localStart.AddMinutes(durationMinutes);
        if (localStart.Date != localEnd.Date)
        {
            return Result.Failure(
                "Appointments.OutsideWorkingHours",
                "Randevu aralığı tek bir yerel gün içinde olmalıdır.");
        }

        if (!(settings?.EnforceWorkingHours ?? ClinicAppointmentSettings.DefaultEnforceWorkingHours))
            return Result.Success();

        var day = localStart.DayOfWeek;
        var source = ClinicWorkingHoursDefaults.Effective(clinicHoursRows);

        var row = source.FirstOrDefault(x => x.DayOfWeek == day);
        if (row is null || row.IsClosed)
        {
            return Result.Failure(
                "Appointments.ClinicClosed",
                "Seçilen gün klinik kapalı.");
        }

        if (row.OpensAt is not { } opensAt || row.ClosesAt is not { } closesAt)
        {
            return Result.Failure(
                "Appointments.OutsideWorkingHours",
                "Klinik çalışma saatleri tanımlı değil.");
        }

        var localStartTime = TimeOnly.FromDateTime(localStart);
        var localEndTime = TimeOnly.FromDateTime(localEnd);
        if (localStartTime < opensAt || localEndTime > closesAt)
        {
            return Result.Failure(
                "Appointments.OutsideWorkingHours",
                "Randevu aralığı klinik çalışma saatleri dışında.");
        }

        if (row.BreakStartsAt is { } breakStart && row.BreakEndsAt is { } breakEnd)
        {
            var breakConflict = localStartTime < breakEnd && breakStart < localEndTime;
            if (breakConflict)
            {
                return Result.Failure(
                    "Appointments.BreakTimeConflict",
                    "Randevu aralığı klinik mola zamanına denk geliyor.");
            }
        }

        return Result.Success();
    }

    internal static DateTime ToClinicLocal(DateTime utcDateTime)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), ClinicTimeZone);

    private static TimeZoneInfo ResolveClinicTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }
}
