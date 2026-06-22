using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Clients;

/// <summary>
/// CQRS-15E: client payment summary'yi Query DB <c>PaymentReadModels</c> üzerinden okur.
/// Aggregate'ler (count, currency totals, last payment date) SQL tarafında hesaplanır; recent satırlar
/// <c>PaidAtUtc DESC, PaymentId DESC</c> ile en fazla <c>RecentTake</c> kadar çekilir. Tüm ödeme satırları belleğe alınmaz.
/// </summary>
public sealed class ClientPaymentSummaryReadModelReader : IClientPaymentSummaryReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public ClientPaymentSummaryReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<ClientPaymentSummaryReadResult> GetSummaryAsync(
        ClientPaymentSummaryReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _queryDb.PaymentReadModels
            .AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && x.ClientId == request.ClientId);

        if (request.ClinicId is { } clinicId)
            baseQuery = baseQuery.Where(x => x.ClinicId == clinicId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return new ClientPaymentSummaryReadResult(
                0,
                Array.Empty<ClientPaymentCurrencyTotalDto>(),
                null,
                Array.Empty<ClientPaymentRecentItemDto>());
        }

        var lastPaymentAtUtc = await baseQuery.MaxAsync(x => (DateTime?)x.PaidAtUtc, cancellationToken);

        // Currency totals SQL tarafında GROUP BY/SUM ile hesaplanır (satırlar belleğe çekilmez); küçük olan
        // currency grubu sıralaması Command DB ile birebir aynı (OrdinalIgnoreCase) olması için bellekte yapılır.
        var currencyGroups = await baseQuery
            .GroupBy(x => x.Currency)
            .Select(g => new { Currency = g.Key, TotalAmount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var currencyTotals = currencyGroups
            .Select(g => new ClientPaymentCurrencyTotalDto(g.Currency, g.TotalAmount))
            .OrderBy(x => x.Currency, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var recentRows = await baseQuery
            .OrderByDescending(x => x.PaidAtUtc)
            .ThenByDescending(x => x.PaymentId)
            .Take(request.RecentTake)
            .ToListAsync(cancellationToken);

        var recent = recentRows.Select(MapRecent).ToList();

        return new ClientPaymentSummaryReadResult(totalCount, currencyTotals, lastPaymentAtUtc, recent);
    }

    private static ClientPaymentRecentItemDto MapRecent(PaymentReadModel x)
        => new(
            x.PaymentId,
            x.PaidAtUtc,
            x.ClinicId,
            x.ClinicName,
            x.PetId,
            x.PetName ?? string.Empty,
            x.Amount,
            x.Currency,
            (PaymentMethod)x.Method,
            x.Notes);
}
