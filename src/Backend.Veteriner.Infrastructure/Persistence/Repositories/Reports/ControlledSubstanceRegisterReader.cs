using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Narkotik ve psikotropik ürünler stok ve sarf defteri: kontrollü sınıftaki ürünlerin bütün stok hareketleri, hareket başına
/// bir satır. Sayfadaki hareketler için geri alınan hareket tipi, uygulama hedefi (hasta/sahip) ve partili düzeltmenin önceki
/// parti bakiyesi birer toplu sorguda okunur (N+1 yok); taraf ve hücre kuralları <see cref="ControlledSubstanceRegisterMapper"/>'dadır.
/// </summary>
public sealed class ControlledSubstanceRegisterReader : IRegisterReader<ControlledSubstanceRegisterItemDto>
{
    private readonly AppDbContext _db;

    public ControlledSubstanceRegisterReader(AppDbContext db)
        => _db = db;

    public async Task<RegisterReadResult<ControlledSubstanceRegisterItemDto>> ReadAsync(
        RegisterReadRequest request,
        CancellationToken ct = default)
    {
        var query =
            from m in StockMovementRegisterScope.Movements(_db, request)
            join p in _db.Products.AsNoTracking() on m.ProductId equals p.Id
            join b in _db.StockBatches.AsNoTracking() on m.BatchId equals b.Id into batches
            from b in batches.DefaultIfEmpty()
            where p.TenantId == request.TenantId && p.ControlledClass != ControlledSubstanceClass.None
            select new { m, p, b };

        var totalCount = await query.CountAsync(ct);
        if (totalCount == 0 || request.Skip >= totalCount)
            return new RegisterReadResult<ControlledSubstanceRegisterItemDto>(totalCount, []);

        var rows = await query
            .OrderBy(x => x.m.OccurredAtUtc).ThenBy(x => x.m.CreatedAtUtc).ThenBy(x => x.m.Id)
            .Skip(request.Skip).Take(request.Take)
            .Select(x => new PageRow(
                x.m.Id,
                x.p.Id,
                x.m.MovementType,
                x.m.OccurredAtUtc,
                x.m.CreatedAtUtc,
                x.m.Quantity,
                x.m.Reason,
                x.m.ReferenceType,
                x.m.ReferenceId,
                x.m.ReversesMovementId,
                x.p.Name,
                x.p.PharmacologicalForm,
                x.p.PresentationForm,
                x.p.Packaging,
                x.m.BatchId,
                x.m.BatchBalanceAfter,
                x.b == null ? null : x.b.LotNumber,
                x.b == null ? null : x.b.SupplierName,
                x.b == null ? null : x.b.InvoiceNumber,
                x.b == null ? null : x.b.ReceivedDate))
            .ToListAsync(ct);

        var reversedTypes = await ReversedTypesAsync(request.TenantId, rows, ct);
        var targets = await AdministrationTargetsAsync(request.TenantId, rows, ct);
        var previousBalances = await PreviousBatchBalancesAsync(request.TenantId, rows, ct);

        var items = rows.Select((r, i) => ControlledSubstanceRegisterMapper.ToItem(
            request.Skip + i + 1,
            new ControlledSubstanceMovement(
                r.MovementId,
                r.ProductId,
                r.MovementType,
                r.OccurredAtUtc,
                r.Quantity,
                r.Reason,
                r.ReferenceType,
                r.ProductName,
                r.PharmacologicalForm,
                r.PresentationForm,
                r.Packaging,
                r.BatchId,
                r.LotNumber,
                r.SupplierName,
                r.InvoiceNumber,
                r.ReceivedDate,
                r.BatchBalanceAfter,
                previousBalances.TryGetValue(r.MovementId, out var previousBalance) ? previousBalance : null,
                r.ReversesMovementId is { } reversedId && reversedTypes.TryGetValue(reversedId, out var reversedType) ? reversedType : null,
                r.ReferenceId is { } referenceId ? targets.GetValueOrDefault(referenceId) : null))).ToList();

        return new RegisterReadResult<ControlledSubstanceRegisterItemDto>(totalCount, items);
    }

    /// <summary>Sayfadaki ters hareketlerin geri aldığı hareketin tipi (geri alma yönünü belirler).</summary>
    private async Task<Dictionary<Guid, StockMovementType>> ReversedTypesAsync(
        Guid tenantId, IReadOnlyList<PageRow> rows, CancellationToken ct)
    {
        var ids = rows.Where(r => r.ReversesMovementId.HasValue).Select(r => r.ReversesMovementId!.Value).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await _db.StockMovements.AsNoTracking()
                .Where(m => m.TenantId == tenantId && ids.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.MovementType, ct);
    }

    /// <summary>Uygulama çıkışlarının kaynak kimliğinden <c>Hasta (Sahip)</c> metni.</summary>
    private async Task<Dictionary<Guid, string>> AdministrationTargetsAsync(
        Guid tenantId, IReadOnlyList<PageRow> rows, CancellationToken ct)
    {
        var ids = rows
            .Where(r => r.MovementType == StockMovementType.Out
                        && r.ReferenceType == Administration.StockReferenceType
                        && r.ReferenceId.HasValue)
            .Select(r => r.ReferenceId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return (await (
                from a in _db.Administrations.AsNoTracking()
                join pet in _db.Pets.AsNoTracking() on a.PetId equals pet.Id
                join c in _db.Clients.AsNoTracking() on pet.ClientId equals c.Id
                where a.TenantId == tenantId && ids.Contains(a.Id)
                select new { a.Id, Pet = pet.Name, Owner = c.FullName }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => $"{x.Pet} ({x.Owner})");
    }

    /// <summary>
    /// Partili düzeltmenin yönü için, aynı partinin bir önceki hareketinin <c>BatchBalanceAfter</c> değeri
    /// (sıra <c>CreatedAtUtc</c>, <c>Id</c>). Yalnız partili <c>Adjustment</c> hareketleri anahtardır.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> PreviousBatchBalancesAsync(
        Guid tenantId, IReadOnlyList<PageRow> rows, CancellationToken ct)
    {
        var adjustments = rows
            .Where(r => r.MovementType == StockMovementType.Adjustment && r.BatchId.HasValue)
            .ToList();
        if (adjustments.Count == 0)
            return [];

        var batchIds = adjustments.Select(r => r.BatchId!.Value).Distinct().ToList();
        var history = (await _db.StockMovements.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.BatchId != null && batchIds.Contains(m.BatchId.Value))
                .Select(m => new { m.Id, BatchId = m.BatchId!.Value, m.CreatedAtUtc, m.BatchBalanceAfter })
                .ToListAsync(ct))
            .GroupBy(m => m.BatchId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).ToList());

        var previous = new Dictionary<Guid, decimal>();
        foreach (var adjustment in adjustments)
        {
            var chain = history[adjustment.BatchId!.Value];
            var index = chain.FindIndex(m => m.Id == adjustment.MovementId);
            if (index > 0 && chain[index - 1].BatchBalanceAfter is { } balance)
                previous[adjustment.MovementId] = balance;
        }

        return previous;
    }

    private sealed record PageRow(
        Guid MovementId,
        Guid ProductId,
        StockMovementType MovementType,
        DateTime OccurredAtUtc,
        DateTime CreatedAtUtc,
        decimal Quantity,
        string? Reason,
        string? ReferenceType,
        Guid? ReferenceId,
        Guid? ReversesMovementId,
        string ProductName,
        string? PharmacologicalForm,
        string? PresentationForm,
        string? Packaging,
        Guid? BatchId,
        decimal? BatchBalanceAfter,
        string? LotNumber,
        string? SupplierName,
        string? InvoiceNumber,
        DateOnly? ReceivedDate);
}
