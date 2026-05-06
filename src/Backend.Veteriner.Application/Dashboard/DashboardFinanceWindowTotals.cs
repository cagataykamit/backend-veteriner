namespace Backend.Veteriner.Application.Dashboard;

/// <summary>
/// Dashboard finans kartları için gün / ISO hafta / takvim ayı toplamları (sunucu tarafı aggregate).
/// </summary>
public sealed record DashboardFinanceWindowTotals(
    decimal TodayTotalPaid,
    int TodayPaymentsCount,
    decimal WeekTotalPaid,
    int WeekPaymentsCount,
    decimal MonthTotalPaid,
    int MonthPaymentsCount);
