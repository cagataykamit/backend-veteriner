using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.WorkingHours;

/// <summary>DB kaydı yokken GET için dönen varsayılan haftalık program.</summary>
public static class ClinicWorkingHoursDefaults
{
    /// <summary>Kliniğin geçerli haftası: kayıt yoksa varsayılan program, varsa kayıtlar (randevu doğrulaması ve müsaitlik ortak).</summary>
    public static IReadOnlyList<ClinicWorkingHourDto> Effective(IReadOnlyList<ClinicWorkingHour> rows)
        => rows.Count == 0
            ? BuildWeek()
            : rows.Select(x => new ClinicWorkingHourDto(
                x.DayOfWeek,
                x.IsClosed,
                x.OpensAt,
                x.ClosesAt,
                x.BreakStartsAt,
                x.BreakEndsAt)).ToList();

    /// <summary>
    /// Çalışma saati kısıtı kapalı klinikte (<see cref="ClinicAppointmentSettings.EnforceWorkingHours"/>) doluluk hesabının penceresi: her
    /// gün 08:00-20:00, molasız. Kapalı gün yoktur; randevu bu pencerenin dışına da verilebilir.
    /// </summary>
    public static IReadOnlyList<ClinicWorkingHourDto> UnrestrictedWeek()
        => Enum.GetValues<DayOfWeek>()
            .Select(day => new ClinicWorkingHourDto(day, false, new TimeOnly(8, 0), new TimeOnly(20, 0), null, null))
            .ToList();

    public static IReadOnlyList<ClinicWorkingHourDto> BuildWeek()
    {
        TimeOnly T(int h, int m = 0) => new(h, m);

        return new[]
        {
            new ClinicWorkingHourDto(DayOfWeek.Monday, false, T(9), T(18), null, null),
            new ClinicWorkingHourDto(DayOfWeek.Tuesday, false, T(9), T(18), null, null),
            new ClinicWorkingHourDto(DayOfWeek.Wednesday, false, T(9), T(18), null, null),
            new ClinicWorkingHourDto(DayOfWeek.Thursday, false, T(9), T(18), null, null),
            new ClinicWorkingHourDto(DayOfWeek.Friday, false, T(9), T(18), null, null),
            new ClinicWorkingHourDto(DayOfWeek.Saturday, false, T(9), T(14), null, null),
            new ClinicWorkingHourDto(DayOfWeek.Sunday, true, null, null, null, null),
        };
    }
}
