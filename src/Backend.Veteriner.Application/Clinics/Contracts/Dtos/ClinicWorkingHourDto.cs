namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>Haftanın bir günü için klinik çalışma saati satırı (GET/PUT gövdesi).</summary>
public sealed record ClinicWorkingHourDto(
    DayOfWeek DayOfWeek,
    bool IsClosed,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt,
    TimeOnly? BreakStartsAt,
    TimeOnly? BreakEndsAt);

/// <summary>
/// <c>GET /clinics/{id}/working-hours</c> yanıtı. <see cref="IsConfigured"/> false: klinik için kayıt yok, <see cref="Items"/> varsayılan haftadır
/// (<c>ClinicWorkingHoursDefaults.BuildWeek</c>); klinik sitesi yayın şartı ve "İlk hafta" adımı kayıt ister, panel varsayılanı ilk kayıtta yazdırır.
/// </summary>
public sealed record ClinicWorkingHoursDto(bool IsConfigured, IReadOnlyList<ClinicWorkingHourDto> Items);
