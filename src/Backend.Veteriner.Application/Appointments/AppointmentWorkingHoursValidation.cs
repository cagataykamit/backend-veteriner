using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.WorkingHours;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Appointments;

internal static class AppointmentWorkingHoursValidation
{
    private static readonly TimeZoneInfo ClinicTimeZone = ResolveClinicTimeZone();

    public static Result Validate(
        DateTime scheduledAtUtc,
        int durationMinutes,
        IReadOnlyList<ClinicWorkingHour> clinicHoursRows)
    {
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(scheduledAtUtc, DateTimeKind.Utc), ClinicTimeZone);
        var localEnd = localStart.AddMinutes(durationMinutes);
        if (localStart.Date != localEnd.Date)
        {
            return Result.Failure(
                "Appointments.OutsideWorkingHours",
                "Randevu aralığı tek bir yerel gün içinde olmalıdır.");
        }

        var day = localStart.DayOfWeek;
        var source = clinicHoursRows.Count == 0
            ? ClinicWorkingHoursDefaults.BuildWeek()
            : clinicHoursRows.Select(x => new ClinicWorkingHourDto(
                x.DayOfWeek,
                x.IsClosed,
                x.OpensAt,
                x.ClosesAt,
                x.BreakStartsAt,
                x.BreakEndsAt)).ToList();

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
