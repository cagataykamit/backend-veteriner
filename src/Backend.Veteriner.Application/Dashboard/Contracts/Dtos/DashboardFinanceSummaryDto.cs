using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

/// <summary>
/// GET /dashboard/finance-summary. Toplamlar İstanbul takvimine göre günlük/haftalık/aylık pencerelerdeki ödemelerin tutar toplamlarıdır;
/// farklı para birimleri aynı toplama eklenir (kur dönüşümü yok).
/// </summary>
public sealed record DashboardFinanceSummaryDto(
    decimal TodayTotalPaid,
    decimal WeekTotalPaid,
    decimal MonthTotalPaid,
    int TodayPaymentsCount,
    int WeekPaymentsCount,
    int MonthPaymentsCount,
    IReadOnlyList<DashboardFinanceRecentPaymentDto> RecentPayments);

public sealed record DashboardFinanceRecentPaymentDto(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string PetName,
    decimal Amount,
    string Currency,
    PaymentMethod Method);
