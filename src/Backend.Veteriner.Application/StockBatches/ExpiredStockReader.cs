using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.StockBatches;

/// <summary>
/// Süresi geçmiş (SKT bugünden önce) parti kalanı: stok okuma uçlarının "kullanılabilir / süresi geçmiş" ayrımının tek kaynağı.
/// Kullanılabilir miktar = toplam − süresi geçmiş; partisiz bakiye kullanılabilirdir.
/// </summary>
public sealed class ExpiredStockReader
{
    private readonly IReadRepository<StockBatch> _batches;
    private readonly TimeProvider _timeProvider;

    public ExpiredStockReader(IReadRepository<StockBatch> batches, TimeProvider timeProvider)
    {
        _batches = batches;
        _timeProvider = timeProvider;
    }

    public DateOnly Today => OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>Klinik+ürün başına süresi geçmiş miktar; süresi geçmiş partisi olmayanlar sözlükte yer almaz.</summary>
    public async Task<IReadOnlyDictionary<(Guid ClinicId, Guid ProductId), decimal>> GetAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> clinicIds,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken ct)
    {
        if (clinicIds.Count == 0 || productIds.Count == 0)
            return new Dictionary<(Guid, Guid), decimal>();

        var expired = await _batches.ListAsync(
            new StockBatchesExpiredReadSpec(tenantId, clinicIds, productIds, Today), ct);

        return expired
            .GroupBy(b => (b.ClinicId, b.ProductId))
            .ToDictionary(g => g.Key, g => g.Sum(b => b.RemainingQuantity));
    }

    public async Task<decimal> GetAsync(Guid tenantId, Guid clinicId, Guid productId, CancellationToken ct)
    {
        var map = await GetAsync(tenantId, [clinicId], [productId], ct);
        return map.GetValueOrDefault((clinicId, productId));
    }

    /// <summary>Kullanılabilir miktar (düşük stok eşiği ve uygulanabilir bakiye bunun üzerinden): toplam − süresi geçmiş, en az 0.</summary>
    public static decimal Available(decimal quantityOnHand, decimal expiredQuantity)
        => Math.Max(0, quantityOnHand - expiredQuantity);
}
