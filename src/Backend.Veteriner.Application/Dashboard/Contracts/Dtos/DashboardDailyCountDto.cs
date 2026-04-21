namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

/// <summary>
/// Dashboard mini-trend (sparkline) satırı: bir İstanbul takvim günü için aggregate adet.
/// <para>
/// <c>date</c> alanı <see cref="DateOnly"/>'dır (JSON'da <c>yyyy-MM-dd</c>) ve **Europe/Istanbul** yerel gününü temsil eder;
/// UTC yoktur. Seri <see cref="DashboardSummaryDto.Last7DaysAppointments"/> içinde **en eskiden en yeniye** (ASC) sıralıdır.
/// </para>
/// </summary>
public sealed record DashboardDailyCountDto(DateOnly Date, int Count);
