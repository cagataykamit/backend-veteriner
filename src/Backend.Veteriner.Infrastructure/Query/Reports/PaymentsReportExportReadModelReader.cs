using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Payments.ReadModels;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Reports;

/// <summary>
/// CQRS-15J + 15N: payment export CSV/XLSX Query DB <c>PaymentReadModels</c> reader.
/// Count SQL tarafında; items yalnızca count &gt; 0 iken ve pipeline limit doğrulamasına uygun şekilde çekilir.
/// Sıralama export Command DB spec'i ile birebir: <c>PaidAtUtc DESC, PaymentId DESC</c>.
/// </summary>
public sealed class PaymentsReportExportReadModelReader : IPaymentsReportExportReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public PaymentsReportExportReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<PaymentsReportExportReadResult> GetExportAsync(
        PaymentsReportExportReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyFilters(_queryDb.PaymentReadModels.AsNoTracking(), request);

        var totalCount = await filtered.CountAsync(cancellationToken);
        if (totalCount == 0)
            return new PaymentsReportExportReadResult(0, Array.Empty<PaymentReportItemDto>());

        if (totalCount > PaymentsReportConstants.MaxExportRows)
            return new PaymentsReportExportReadResult(totalCount, Array.Empty<PaymentReportItemDto>());

        var rows = await filtered
            .OrderByDescending(x => x.PaidAtUtc)
            .ThenByDescending(x => x.PaymentId)
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapItem).ToList();
        return new PaymentsReportExportReadResult(totalCount, items);
    }

    private static IQueryable<PaymentReadModel> ApplyFilters(
        IQueryable<PaymentReadModel> query,
        PaymentsReportExportReadRequest request)
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

        if (request.SearchContainsLikePattern is { } pattern)
        {
            query = ApplyExportSearchFilter(
                query,
                pattern,
                request.SearchMatchClientIds ?? [],
                request.SearchMatchPetIds ?? []);
        }

        return query;
    }

    /// <summary>
    /// CQRS-15N: direct normalized alanlar + lookup ID filtreleri (Command export search OR mantığı ile hizalı).
    /// </summary>
    private static IQueryable<PaymentReadModel> ApplyExportSearchFilter(
        IQueryable<PaymentReadModel> query,
        string pattern,
        IReadOnlyList<Guid> searchClientIds,
        IReadOnlyList<Guid> searchPetIds)
    {
        var cids = searchClientIds;
        var pids = searchPetIds;
        return query.Where(x =>
            EF.Functions.Like(x.ClientNameNormalized, pattern)
            || (x.PetNameNormalized != null && EF.Functions.Like(x.PetNameNormalized, pattern))
            || (x.NotesNormalized != null && EF.Functions.Like(x.NotesNormalized, pattern))
            || EF.Functions.Like(x.Currency, pattern)
            || (cids.Count > 0 && cids.Contains(x.ClientId))
            || (x.PetId != null && pids.Count > 0 && pids.Contains(x.PetId.Value)));
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
