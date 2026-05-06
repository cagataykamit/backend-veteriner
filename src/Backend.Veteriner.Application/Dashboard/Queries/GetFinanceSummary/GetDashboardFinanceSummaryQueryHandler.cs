using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;

public sealed class GetDashboardFinanceSummaryQueryHandler
    : IRequestHandler<GetDashboardFinanceSummaryQuery, Result<DashboardFinanceSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IDashboardFinancePaymentAggregatesReader _financeAggregates;
    private readonly ILogger<GetDashboardFinanceSummaryQueryHandler> _logger;

    public GetDashboardFinanceSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Payment> payments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IDashboardFinancePaymentAggregatesReader financeAggregates,
        ILogger<GetDashboardFinanceSummaryQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _payments = payments;
        _clients = clients;
        _pets = pets;
        _financeAggregates = financeAggregates;
        _logger = logger ?? NullLogger<GetDashboardFinanceSummaryQueryHandler>.Instance;
    }

    public async Task<Result<DashboardFinanceSummaryDto>> Handle(
        GetDashboardFinanceSummaryQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<DashboardFinanceSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var utcNow = DateTime.UtcNow;
        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(utcNow);
        var (weekStart, weekEnd) = OperationPeriodBounds.WeekForUtcNow(utcNow);
        var (monthStart, monthEnd) = OperationPeriodBounds.MonthForUtcNow(utcNow);
        var trendBuckets = OperationPeriodBounds.Last7DaysForUtcNow(utcNow);
        var trendStartUtc = trendBuckets[0].StartUtcInclusive;
        var trendEndUtc = trendBuckets[^1].EndUtcExclusive;
        var clinicId = _clinicContext.ClinicId;
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        var totals = await _financeAggregates.GetTotalsAsync(
            tenantId,
            clinicId,
            dayStart,
            dayEnd,
            weekStart,
            weekEnd,
            monthStart,
            monthEnd,
            ct);
        MarkStep("financeWindowAggregates");

        var todayTotalPaid = totals.TodayTotalPaid;
        var todayPaymentsCount = totals.TodayPaymentsCount;
        var weekTotalPaid = totals.WeekTotalPaid;
        var weekPaymentsCount = totals.WeekPaymentsCount;
        var monthTotalPaid = totals.MonthTotalPaid;
        var monthPaymentsCount = totals.MonthPaymentsCount;

        var trendProjectionRows = await _payments.ListAsync(
            new PaymentsPaidAtAmountInWindowSpec(tenantId, clinicId, trendStartUtc, trendEndUtc),
            ct);
        MarkStep("last7DaysPaid");
        var last7DaysPaid = BuildDailyTotals(trendBuckets, trendProjectionRows);

        var recentRows = await _payments.ListAsync(
            new PaymentsForDashboardRecentSpec(tenantId, clinicId, DashboardFinanceSummaryConstants.RecentPaymentsTake),
            ct);
        MarkStep("recentPayments");

        var clientIds = recentRows.Select(r => r.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        MarkStep("recentClientsLookup");
        var clientNameById = clients.ToDictionary(c => c.Id, c => c.FullName);

        var petIds = recentRows
            .Where(r => r.PetId.HasValue)
            .Select(r => r.PetId!.Value)
            .Distinct()
            .ToArray();
        var petNameById = new Dictionary<Guid, string>();
        if (petIds.Length > 0)
        {
            var pets = await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct);
            MarkStep("recentPetsLookup");
            petNameById = pets.ToDictionary(p => p.Id, p => p.Name);
        }

        var recentDtos = recentRows
            .Select(r => new DashboardFinanceRecentPaymentDto(
                r.Id,
                r.PaidAtUtc,
                r.ClientId,
                clientNameById.GetValueOrDefault(r.ClientId, string.Empty),
                r.PetId,
                r.PetId is { } pid ? petNameById.GetValueOrDefault(pid, string.Empty) : string.Empty,
                r.Amount,
                r.Currency,
                r.Method))
            .ToList();

        var dto = new DashboardFinanceSummaryDto(
            todayTotalPaid,
            weekTotalPaid,
            monthTotalPaid,
            todayPaymentsCount,
            weekPaymentsCount,
            monthPaymentsCount,
            recentDtos,
            last7DaysPaid);

        _logger.LogInformation(
            "Dashboard finance summary generated. TenantId={TenantId} ClinicId={ClinicId} UtcNow={UtcNow} DayWindowUtc={DayStartUtc}..{DayEndUtc} WeekWindowUtc={WeekStartUtc}..{WeekEndUtc} MonthWindowUtc={MonthStartUtc}..{MonthEndUtc} TrendWindowUtc={TrendStartUtc}..{TrendEndUtc} TrendProjectionRows={TrendProjectionRows} TodayCount={TodayCount} WeekCount={WeekCount} MonthCount={MonthCount} RecentPayments={RecentPayments} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            clinicId,
            utcNow,
            dayStart,
            dayEnd,
            weekStart,
            weekEnd,
            monthStart,
            monthEnd,
            trendStartUtc,
            trendEndUtc,
            trendProjectionRows.Count,
            todayPaymentsCount,
            weekPaymentsCount,
            monthPaymentsCount,
            recentDtos.Count,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<DashboardFinanceSummaryDto>.Success(dto);
    }

    /// <summary>
    /// Ödeme tutarlarını 7 günlük İstanbul bucket'larına [start, end) aralığı ile eşler; sonuç oldest→newest
    /// sıralı tam 7 eleman döner, boş günler <c>0m</c> ile doldurulur. Mixed-currency davranışı §27.6 ile aynıdır.
    /// </summary>
    private static List<DashboardDailyTotalDto> BuildDailyTotals(
        IReadOnlyList<OperationPeriodBounds.DailyWindow> buckets,
        IReadOnlyList<PaymentPaidAtAmountRow> rows)
    {
        var totals = new decimal[buckets.Count];
        foreach (var row in rows)
        {
            for (var i = 0; i < buckets.Count; i++)
            {
                if (row.PaidAtUtc >= buckets[i].StartUtcInclusive && row.PaidAtUtc < buckets[i].EndUtcExclusive)
                {
                    totals[i] += row.Amount;
                    break;
                }
            }
        }

        var result = new List<DashboardDailyTotalDto>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
            result.Add(new DashboardDailyTotalDto(buckets[i].LocalDate, totals[i]));
        return result;
    }
}
