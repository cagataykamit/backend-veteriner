using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.DailyTransactions;

namespace Backend.Veteriner.Application.Reports.Performance;

/// <summary>
/// İşletme performansı özeti (REPORT-008 dilim 1, docs/PERFORMANCE_SUMMARY_API_CONTRACT.md). Her bölüm ilgili raporun
/// hesabından gelir; kullanıcının izni olmayan bölüm <c>null</c> döner.
/// </summary>
public sealed record PerformanceSummaryDto(
    string Currency,
    PerformancePeriodDto Current,
    PerformancePeriodDto? Previous,
    PerformanceOutstandingDto? Outstanding);

/// <summary>Bir dönemin göstergeleri; <c>From</c>/<c>To</c> İstanbul takvim günleri, ikisi dahil.</summary>
public sealed record PerformancePeriodDto(
    DateOnly From,
    DateOnly To,
    DailyTransactionTotalsDto? Charges,
    PerformanceCashDto? Cash,
    int? ExaminationCount,
    PerformanceVaccinationCountsDto? Vaccinations,
    AppointmentReportStatusCountsDto? Appointments);

/// <summary>Kasa özetinin pano için gereken kısmı (iptal edilen ödeme listesi taşınmaz).</summary>
public sealed record PerformanceCashDto(
    CashSummaryAmountsDto Totals,
    IReadOnlyList<CashSummaryMethodDto> ByMethod,
    IReadOnlyList<CashSummaryDayDto> ByDay,
    IReadOnlyList<CashSummaryUserDto> ByUser)
{
    public static PerformanceCashDto From(CashSummaryReportDto summary)
        => new(summary.Totals, summary.ByMethod, summary.ByDay, summary.ByUser);
}

/// <summary>Uygulanan (uygulama tarihi aralıkta) ve planlanan (vade tarihi aralıkta) aşı sayısı; aşı raporuyla aynı tarih ekseni.</summary>
public sealed record PerformanceVaccinationCountsDto(int Applied, int Scheduled);

/// <summary>Bugünkü açık alacak; tarih aralığından bağımsızdır (borç raporu).</summary>
public sealed record PerformanceOutstandingDto(decimal TotalOutstandingAmount, int ClientCount);
