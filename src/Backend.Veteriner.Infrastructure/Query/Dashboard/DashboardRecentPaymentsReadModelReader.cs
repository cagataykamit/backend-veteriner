using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Dashboard;

public sealed class DashboardRecentPaymentsReadModelReader : IDashboardRecentPaymentsReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public DashboardRecentPaymentsReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<IReadOnlyList<DashboardFinanceRecentPaymentDto>> GetRecentAsync(
        DashboardRecentPaymentsReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var rows = await _queryDb.PaymentReadModels
            .AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && x.ClinicId == request.ClinicId)
            .OrderByDescending(x => x.PaidAtUtc)
            .ThenByDescending(x => x.PaymentId)
            .Take(request.Take)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    private static DashboardFinanceRecentPaymentDto Map(PaymentReadModel x)
        => new(
            x.PaymentId,
            x.PaidAtUtc,
            x.ClientId,
            x.ClientName,
            x.PetId,
            x.PetName ?? string.Empty,
            x.Amount,
            x.Currency,
            (PaymentMethod)x.Method);
}
