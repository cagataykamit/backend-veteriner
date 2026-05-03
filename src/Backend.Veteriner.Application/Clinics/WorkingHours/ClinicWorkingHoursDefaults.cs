using Backend.Veteriner.Application.Clinics.Contracts.Dtos;

namespace Backend.Veteriner.Application.Clinics.WorkingHours;

/// <summary>DB kaydı yokken GET için dönen varsayılan haftalık program.</summary>
public static class ClinicWorkingHoursDefaults
{
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
