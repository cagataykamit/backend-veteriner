namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>Haftanın bir günü için klinik çalışma saati satırı (GET/PUT gövdesi).</summary>
public sealed record ClinicWorkingHourDto(
    DayOfWeek DayOfWeek,
    bool IsClosed,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt,
    TimeOnly? BreakStartsAt,
    TimeOnly? BreakEndsAt);
