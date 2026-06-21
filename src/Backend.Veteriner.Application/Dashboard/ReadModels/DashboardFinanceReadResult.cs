using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

namespace Backend.Veteriner.Application.Dashboard.ReadModels;

public sealed record DashboardFinanceReadResult(
    DashboardFinanceWindowTotals WindowTotals,
    IReadOnlyList<DashboardDailyTotalDto> LastSevenDaysPaid);
