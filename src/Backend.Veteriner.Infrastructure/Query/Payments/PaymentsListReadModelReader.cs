using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Payments;

public sealed class PaymentsListReadModelReader : IPaymentsListReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public PaymentsListReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<PaymentsListReadResult> GetListAsync(
        PaymentsListReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyListFilters(_queryDb.PaymentReadModels.AsNoTracking(), request);

        var total = await filtered.CountAsync(cancellationToken);

        var rows = await filtered
            .OrderByDescending(x => x.PaidAtUtc)
            .ThenByDescending(x => x.PaymentId)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapListItem).ToList();
        return new PaymentsListReadResult(items, total);
    }

    private static IQueryable<PaymentReadModel> ApplyListFilters(
        IQueryable<PaymentReadModel> query,
        PaymentsListReadRequest request)
    {
        query = query.Where(x =>
            x.TenantId == request.TenantId
            && x.ClinicId == request.ClinicId);

        if (request.ClientId.HasValue)
            query = query.Where(x => x.ClientId == request.ClientId.Value);

        if (request.PetId.HasValue)
            query = query.Where(x => x.PetId == request.PetId.Value);

        if (request.Method.HasValue)
            query = query.Where(x => x.Method == (int)request.Method.Value);

        if (request.PaidFromUtc.HasValue)
            query = query.Where(x => x.PaidAtUtc >= request.PaidFromUtc.Value);

        if (request.PaidToUtc.HasValue)
            query = query.Where(x => x.PaidAtUtc <= request.PaidToUtc.Value);

        if (request.SearchContainsLikePattern is { } pattern)
            query = ApplyListSearchFilter(query, pattern);

        return query;
    }

    /// <summary>
    /// Command DB <see cref="Application.Payments.Specs.PaymentsListFilteredPagedSpec"/> ile aynı alan kümesi;
    /// denormalize normalized alanlar üzerinden (client/pet lookup yok).
    /// </summary>
    private static IQueryable<PaymentReadModel> ApplyListSearchFilter(
        IQueryable<PaymentReadModel> query,
        string pattern)
        => query.Where(x =>
            EF.Functions.Like(x.ClientNameNormalized, pattern)
            || (x.PetNameNormalized != null && EF.Functions.Like(x.PetNameNormalized, pattern))
            || (x.NotesNormalized != null && EF.Functions.Like(x.NotesNormalized, pattern))
            || EF.Functions.Like(x.Currency, pattern));

    private static PaymentListItemDto MapListItem(PaymentReadModel x)
        => new(
            x.PaymentId,
            x.ClinicId,
            x.ClientId,
            x.ClientName,
            x.PetId,
            x.PetName ?? string.Empty,
            x.Amount,
            x.Currency,
            (PaymentMethod)x.Method,
            x.PaidAtUtc);
}
