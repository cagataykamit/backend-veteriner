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
    IReadOnlyList<DashboardFinanceRecentPaymentDto> RecentPayments,
    // Faz 6B: son 7 takvim gününün tahsilat toplamları (sparkline için). İstanbul yerel günü bazında,
    // bugün dahil, oldest→newest sıralı, tam 7 eleman — boş günler 0 ile doldurulur.
    // Mixed-currency notu §27.6'daki çizgi ile aynı: farklı para birimli ödemeler ayrım yapılmadan toplanır.
    IReadOnlyList<DashboardDailyTotalDto> Last7DaysPaid);

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
