using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Dashboard;

public sealed class DashboardFinanceReadModelReader : IDashboardFinanceReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public DashboardFinanceReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<DashboardFinanceReadResult> GetAsync(
        DashboardFinanceReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var weekDates = request.WeekLocalDatesInclusive.ToHashSet();
        var monthDates = request.MonthLocalDatesInclusive.ToHashSet();
        var trendDates = request.LastSevenDayBuckets.Select(b => b.LocalDate).ToHashSet();
        var relevantDates = monthDates.Union(trendDates).ToArray();

        var stats = await LoadDailyStatsAsync(request.TenantId, request.ClinicId, relevantDates, cancellationToken);

        var todayTotalPaid = 0m;
        var todayPaymentsCount = 0;
        var weekTotalPaid = 0m;
        var weekPaymentsCount = 0;
        var monthTotalPaid = 0m;
        var monthPaymentsCount = 0;

        foreach (var row in stats)
        {
            if (row.LocalDate == request.TodayLocalDate)
            {
                todayTotalPaid += row.PaidTotalAmount;
                todayPaymentsCount += row.PaidCount;
            }

            if (weekDates.Contains(row.LocalDate))
            {
                weekTotalPaid += row.PaidTotalAmount;
                weekPaymentsCount += row.PaidCount;
            }

            if (monthDates.Contains(row.LocalDate))
            {
                monthTotalPaid += row.PaidTotalAmount;
                monthPaymentsCount += row.PaidCount;
            }
        }

        var trendTotalsByDate = stats
            .Where(x => trendDates.Contains(x.LocalDate))
            .GroupBy(x => x.LocalDate)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.PaidTotalAmount));

        var lastSevenDaysPaid = request.LastSevenDayBuckets
            .Select(b => new DashboardDailyTotalDto(b.LocalDate, trendTotalsByDate.GetValueOrDefault(b.LocalDate, 0m)))
            .ToList();

        return new DashboardFinanceReadResult(
            new DashboardFinanceWindowTotals(
                todayTotalPaid,
                todayPaymentsCount,
                weekTotalPaid,
                weekPaymentsCount,
                monthTotalPaid,
                monthPaymentsCount),
            lastSevenDaysPaid);
    }

    private async Task<List<ClinicDailyPaymentStatsReadModel>> LoadDailyStatsAsync(
        Guid tenantId,
        Guid? clinicId,
        DateOnly[] relevantDates,
        CancellationToken cancellationToken)
    {
        if (relevantDates.Length == 0)
            return [];

        var query = _queryDb.ClinicDailyPaymentStatsReadModels.AsNoTracking()
            .Where(x => x.TenantId == tenantId && relevantDates.Contains(x.LocalDate));

        if (clinicId is { } scopedClinicId)
            query = query.Where(x => x.ClinicId == scopedClinicId);

        return await query.ToListAsync(cancellationToken);
    }
}
