using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Reports;

/// <summary>
/// CQRS-15G: payment report JSON'u Query DB <c>PaymentReadModels</c> üzerinden okur.
/// <para>
/// Aggregate'ler SQL tarafında hesaplanır: <c>TotalCount = COUNT(*)</c>, <c>TotalAmount = SUM(Amount)</c> (boşsa 0).
/// Items <c>PaidAtUtc DESC, PaymentId DESC</c> ile sayfalanır (Command DB report spec'i ile birebir). Filtre kümesi
/// mevcut report JSON Command DB davranışı ile aynıdır (date range + clinic + client + pet + method); search desteklenmez.
/// Tüm satırlar sırf aggregate için belleğe çekilmez.
/// </para>
/// </summary>
public sealed class PaymentsReportReadModelReader : IPaymentsReportReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public PaymentsReportReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<PaymentsReportReadResult> GetReportAsync(
        PaymentsReportReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyFilters(_queryDb.PaymentReadModels.AsNoTracking(), request);

        var totalCount = await filtered.CountAsync(cancellationToken);
        if (totalCount == 0)
            return new PaymentsReportReadResult(0, 0m, Array.Empty<PaymentReportItemDto>());

        // SUM SQL tarafında; boş küme NULL döndüğü için nullable toplanıp 0'a coalesce edilir (Command DB .Sum() ile aynı).
        var totalAmount = await filtered.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var rows = await filtered
            .OrderByDescending(x => x.PaidAtUtc)
            .ThenByDescending(x => x.PaymentId)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapItem).ToList();
        return new PaymentsReportReadResult(totalCount, totalAmount, items);
    }

    private static IQueryable<PaymentReadModel> ApplyFilters(
        IQueryable<PaymentReadModel> query,
        PaymentsReportReadRequest request)
    {
        query = query.Where(x => x.TenantId == request.TenantId);

        if (request.ClinicId is { } clinicId)
            query = query.Where(x => x.ClinicId == clinicId);

        if (request.ClientId is { } clientId)
            query = query.Where(x => x.ClientId == clientId);

        if (request.PetId is { } petId)
            query = query.Where(x => x.PetId == petId);

        if (request.Method is { } method)
            query = query.Where(x => x.Method == (int)method);

        query = query.Where(x => x.PaidAtUtc >= request.FromUtc && x.PaidAtUtc <= request.ToUtc);

        return query;
    }

    private static PaymentReportItemDto MapItem(PaymentReadModel x)
        => new(
            x.PaymentId,
            x.PaidAtUtc,
            x.ClinicId,
            x.ClinicName,
            x.ClientId,
            x.ClientName,
            x.PetId,
            x.PetName ?? string.Empty,
            x.Amount,
            x.Currency,
            (PaymentMethod)x.Method,
            x.Notes);
}
