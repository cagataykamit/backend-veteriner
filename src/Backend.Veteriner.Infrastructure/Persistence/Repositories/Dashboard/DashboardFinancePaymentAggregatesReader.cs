using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Dashboard;

public sealed class DashboardFinancePaymentAggregatesReader : IDashboardFinancePaymentAggregatesReader
{
    private readonly AppDbContext _db;

    public DashboardFinancePaymentAggregatesReader(AppDbContext db)
        => _db = db;

    public async Task<DashboardFinanceWindowTotals> GetTotalsAsync(
        Guid tenantId,
        Guid? clinicId,
        DateTime dayStartUtc,
        DateTime dayEndUtcExclusive,
        DateTime weekStartUtc,
        DateTime weekEndUtcExclusive,
        DateTime monthStartUtc,
        DateTime monthEndUtcExclusive,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        CancellationToken ct = default)
    {
        IQueryable<Payment> Base()
        {
            var q = _db.Payments.AsNoTracking().Where(p => p.TenantId == tenantId);
            if (clinicId.HasValue)
                q = q.Where(p => p.ClinicId == clinicId.Value);
            else if (accessibleClinicIds is { Count: > 0 })
                q = q.Where(p => accessibleClinicIds.Contains(p.ClinicId));
            else if (accessibleClinicIds is { Count: 0 })
                q = q.Where(_ => false);
            return q;
        }

        var todayQ = Base().Where(p =>
            p.PaidAtUtc >= dayStartUtc && p.PaidAtUtc < dayEndUtcExclusive);
        var todayTotalPaid = await todayQ.SumAsync(p => p.Amount, ct);
        var todayPaymentsCount = await todayQ.CountAsync(ct);

        var weekQ = Base().Where(p =>
            p.PaidAtUtc >= weekStartUtc && p.PaidAtUtc < weekEndUtcExclusive);
        var weekTotalPaid = await weekQ.SumAsync(p => p.Amount, ct);
        var weekPaymentsCount = await weekQ.CountAsync(ct);

        var monthQ = Base().Where(p =>
            p.PaidAtUtc >= monthStartUtc && p.PaidAtUtc < monthEndUtcExclusive);
        var monthTotalPaid = await monthQ.SumAsync(p => p.Amount, ct);
        var monthPaymentsCount = await monthQ.CountAsync(ct);

        return new DashboardFinanceWindowTotals(
            todayTotalPaid,
            todayPaymentsCount,
            weekTotalPaid,
            weekPaymentsCount,
            monthTotalPaid,
            monthPaymentsCount);
    }
}
