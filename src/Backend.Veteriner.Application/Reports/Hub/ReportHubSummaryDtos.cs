using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.Hub;

/// <summary>
/// Rapor Merkezi ana ekran özeti (ADR-052, REPORT_HUB_SUMMARY_API_CONTRACT.md). Bölüm, aynı veriyi gösteren raporun izni yoksa <c>null</c>.
/// <c>TodayCash</c>, <c>Outstanding</c> ve <c>Attention</c> dönemden bağımsızdır.
/// </summary>
public sealed record ReportHubSummaryDto(
    string Period,
    Guid ClinicId,
    string Currency,
    DateTime GeneratedAtUtc,
    ReportHubWindowDto Window,
    ReportHubTodayCashDto? TodayCash,
    ReportHubTurnoverDto? Turnover,
    ReportHubCollectionsDto? Collections,
    ReportHubOutstandingDto? Outstanding,
    ReportHubAppointmentsDto? Appointments,
    ReportHubExaminationsDto? Examinations,
    ReportHubVaccinationsDto? Vaccinations,
    ReportHubAttentionDto Attention);

/// <summary>Bu dönemin ve karşılaştırma döneminin takvim günleri (ikisi dahil); saat kesimi şimdidir.</summary>
public sealed record ReportHubWindowDto(DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo);

/// <summary>Eğilim noktası; <c>From</c>/<c>To</c> İstanbul günleri, ikisi dahil. Hareketsiz nokta 0.</summary>
public sealed record ReportHubSeriesPointDto(DateOnly From, DateOnly To, decimal Value);

/// <param name="MainRegisterBalance">Ana kasanın şu anki bakiyesi; <c>CashRegisters.Read</c> yoksa null.</param>
public sealed record ReportHubTodayCashDto(
    decimal NetCollectedAmount,
    int CollectedCount,
    IReadOnlyList<ReportHubSeriesPointDto> Last7Days,
    decimal? MainRegisterBalance);

public sealed record ReportHubTurnoverDto(
    decimal NetChargeAmount,
    decimal PreviousNetChargeAmount,
    IReadOnlyList<ReportHubSeriesPointDto> Series);

public sealed record ReportHubCollectionsDto(decimal NetAmount, int CollectedCount, IReadOnlyList<ReportHubMethodAmountDto> ByMethod);

public sealed record ReportHubMethodAmountDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod PaymentMethod,
    decimal NetAmount);

public sealed record ReportHubOutstandingDto(decimal TotalOutstandingAmount, int ClientCount, decimal MaxClientOutstandingAmount);

public sealed record ReportHubAppointmentsDto(
    int TotalCount,
    int NoShowCount,
    AppointmentReportStatusCountsDto StatusCounts,
    IReadOnlyList<ReportHubSeriesPointDto> Series);

public sealed record ReportHubExaminationsDto(int Count, IReadOnlyList<ReportHubSeriesPointDto> Series);

/// <param name="UpcomingCount">Önümüzdeki 7 gün planlı aşı (dönemden bağımsız).</param>
public sealed record ReportHubVaccinationsDto(int AppliedCount, int UpcomingCount);

/// <summary>"Bakmanız gerekenler" bandı; her alan kendi izniyle dolar, yoksa null.</summary>
/// <param name="OverdueAppointmentCount">Bugün'ün <c>openItems.overdueAppointmentCount</c>'u (B-02); <c>Visits.Read</c> ve <c>Appointments.Read</c>.</param>
public sealed record ReportHubAttentionDto(
    ReportHubPendingDto? HbsNotEnteredPrescriptions,
    ReportHubPendingDto? HbsNotEnteredVaccinations,
    int? EndOfDayUnchargedVisitCount,
    int? OverdueVaccinationCount,
    int? LowStockProductCount,
    int? OverdueAppointmentCount);

/// <param name="OldestAtUtc">Penceredeki en eski kaydın tarihi; sayı 0 ise null.</param>
public sealed record ReportHubPendingDto(int Count, DateTime? OldestAtUtc);
