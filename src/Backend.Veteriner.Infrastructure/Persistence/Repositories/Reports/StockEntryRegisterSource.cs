using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>Stok girişi (<c>In</c>/<c>Initial</c>) satırı: ürün + (varsa) parti alanları. Aşı ve ilaç defterlerinin ortak ham satırı.</summary>
internal sealed record StockEntryRow(
    Guid MovementId,
    Guid ProductId,
    DateTime OccurredAtUtc,
    string ProductName,
    string? PharmacologicalForm,
    string? PresentationForm,
    string Unit,
    decimal Quantity,
    string? LotNumber,
    DateOnly? ExpiryDate,
    string? SupplierName,
    string? TransportMethod,
    string? ColdChainMonitor,
    DateOnly? DispatchedDate,
    DateOnly? ReceivedDate,
    DateOnly? InvoiceDate,
    string? InvoiceNumber,
    bool IsBatched,
    bool IsReversed);

/// <summary>
/// Aşı ve ilaç kayıt defterlerinin ortak okuması: <paramref name="kind"/> sınıfındaki ürünlerin giriş hareketleri,
/// partisi varsa partiyle (sol birleşim). Geri alınmış girişler <see cref="RegisterReadRequest.IncludeVoided"/> olmadıkça elenir.
/// Sıra: <c>OccurredAtUtc</c>, <c>CreatedAtUtc</c>, hareket <c>Id</c>. Sözleşme bölüm 13-14.
/// </summary>
internal static class StockEntryRegisterSource
{
    public static async Task<(int TotalCount, IReadOnlyList<StockEntryRow> Rows)> ReadAsync(
        AppDbContext db,
        RegisterReadRequest request,
        ProductKind kind,
        CancellationToken ct)
    {
        var entries = StockMovementRegisterScope.Movements(db, request)
            .Where(m => m.MovementType == StockMovementType.Initial || m.MovementType == StockMovementType.In);

        var query =
            from m in entries
            join p in db.Products.AsNoTracking() on m.ProductId equals p.Id
            join b in db.StockBatches.AsNoTracking() on m.BatchId equals b.Id into batches
            from b in batches.DefaultIfEmpty()
            let reversed = db.StockMovements.Any(r => r.TenantId == request.TenantId && r.ReversesMovementId == m.Id)
            where p.TenantId == request.TenantId
                  && p.Kind == kind
                  && (request.IncludeVoided || !reversed)
            select new { m, p, b, reversed };

        var totalCount = await query.CountAsync(ct);
        if (totalCount == 0 || request.Skip >= totalCount)
            return (totalCount, []);

        var rows = await query
            .OrderBy(x => x.m.OccurredAtUtc).ThenBy(x => x.m.CreatedAtUtc).ThenBy(x => x.m.Id)
            .Skip(request.Skip).Take(request.Take)
            .Select(x => new StockEntryRow(
                x.m.Id,
                x.p.Id,
                x.m.OccurredAtUtc,
                x.p.Name,
                x.p.PharmacologicalForm,
                x.p.PresentationForm,
                x.p.Unit,
                x.m.Quantity,
                x.b == null ? null : x.b.LotNumber,
                x.b == null ? null : x.b.ExpiryDate,
                x.b == null ? null : x.b.SupplierName,
                x.b == null ? null : x.b.TransportMethod,
                x.b == null ? null : x.b.ColdChainMonitor,
                x.b == null ? null : x.b.DispatchedDate,
                x.b == null ? null : x.b.ReceivedDate,
                x.b == null ? null : x.b.InvoiceDate,
                x.b == null ? null : x.b.InvoiceNumber,
                x.b != null,
                x.reversed))
            .ToListAsync(ct);

        return (totalCount, rows);
    }
}
