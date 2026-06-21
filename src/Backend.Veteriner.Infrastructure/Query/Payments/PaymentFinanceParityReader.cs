using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Payments;

/// <summary>
/// Command DB <c>Payments</c> ile Query DB contribution + daily stats parity okuması.
/// Salt okunur (<c>AsNoTracking</c>).
/// </summary>
public sealed class PaymentFinanceParityReader : IPaymentFinanceParityReader
{
    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;

    public PaymentFinanceParityReader(AppDbContext commandDb, QueryDbContext queryDb)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
    }

    public async Task<PaymentFinanceParityResult> GetGlobalParityAsync(
        CancellationToken cancellationToken = default)
        => await BuildParityAsync(scopeTenantId: null, cancellationToken);

    public async Task<PaymentFinanceParityResult> GetTenantParityAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
        => await BuildParityAsync(scopeTenantId: tenantId, cancellationToken);

    private async Task<PaymentFinanceParityResult> BuildParityAsync(
        Guid? scopeTenantId,
        CancellationToken cancellationToken)
    {
        var commandPaymentsQuery = _commandDb.Payments.AsNoTracking();
        var queryContributionsQuery = _queryDb.PaymentDailyContributionReadModels.AsNoTracking();
        var queryDailyStatsQuery = _queryDb.ClinicDailyPaymentStatsReadModels.AsNoTracking();

        if (scopeTenantId is { } tenantId)
        {
            commandPaymentsQuery = commandPaymentsQuery.Where(p => p.TenantId == tenantId);
            queryContributionsQuery = queryContributionsQuery.Where(x => x.TenantId == tenantId);
            queryDailyStatsQuery = queryDailyStatsQuery.Where(x => x.TenantId == tenantId);
        }

        var commandPaymentCount = await commandPaymentsQuery.LongCountAsync(cancellationToken);
        var queryContributionCount = await queryContributionsQuery.LongCountAsync(cancellationToken);

        var commandPayments = await commandPaymentsQuery
            .Select(p => new { p.TenantId, p.ClinicId, p.PaidAtUtc, p.Currency, p.Amount })
            .ToListAsync(cancellationToken);

        var commandBuckets = commandPayments
            .GroupBy(p => new
            {
                p.TenantId,
                p.ClinicId,
                LocalDate = OperationDayBounds.ToLocalDate(p.PaidAtUtc),
                p.Currency
            })
            .Select(g => new PaymentFinanceParityEvaluator.DailyBucketSnapshot(
                g.Key.TenantId,
                g.Key.ClinicId,
                g.Key.LocalDate,
                g.Key.Currency,
                g.Sum(x => x.Amount),
                g.Count()))
            .ToList();

        var queryBuckets = await queryDailyStatsQuery
            .Select(x => new PaymentFinanceParityEvaluator.DailyBucketSnapshot(
                x.TenantId,
                x.ClinicId,
                x.LocalDate,
                x.Currency,
                x.PaidTotalAmount,
                x.PaidCount))
            .ToListAsync(cancellationToken);

        return PaymentFinanceParityEvaluator.Evaluate(
            commandPaymentCount,
            queryContributionCount,
            commandBuckets,
            queryBuckets,
            scopeTenantId);
    }
}
