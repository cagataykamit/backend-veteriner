using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.StockBatches;

/// <summary>
/// Düşüm için parti seçimi ve partisiz bakiye hesabı (kural tek yerde): hem stok hareketi hem uygulama kaydı kullanır.
/// Partisiz bakiye ayrı kayıt değildir: <c>ProductStock.QuantityOnHand − Σ parti kalanı</c>.
/// </summary>
public sealed class StockBatchResolver
{
    public const string InsufficientStockCode = "StockMovements.InsufficientStock";
    public const string NotFoundCode = "StockBatches.NotFound";
    public const string ExpiredCode = "StockBatches.Expired";

    private readonly IRepository<StockBatch> _batches;

    public StockBatchResolver(IRepository<StockBatch> batches) => _batches = batches;

    /// <summary>
    /// <paramref name="quantity"/> kadar düşülecek partiyi seçer (izlenen). Başarılı ve <c>null</c> = partisiz bakiyeden düşülür.
    /// <paramref name="requestedBatchId"/> verilirse o parti doğrulanır; yoksa FEFO ile ilk yeterli parti, o da yoksa yeterli partisiz bakiye.
    /// <paramref name="notExpiredOn"/> verilirse (uygulama günü) süresi geçmiş partiler kullanılamaz.
    /// </summary>
    public async Task<Result<StockBatch?>> ResolveForConsumptionAsync(
        Guid tenantId,
        Guid clinicId,
        Guid productId,
        Guid? requestedBatchId,
        decimal quantity,
        decimal quantityOnHand,
        DateOnly? notExpiredOn,
        CancellationToken ct)
    {
        if (requestedBatchId is { } batchId)
        {
            var requested = await FindAsync(tenantId, clinicId, productId, batchId, ct);
            if (requested is null)
                return Result<StockBatch?>.Failure(NotFoundCode, "Parti bulunamadı veya ürün/klinikle uyuşmuyor.");
            if (notExpiredOn is { } day && requested.IsExpiredOn(day))
                return Result<StockBatch?>.Failure(ExpiredCode, "Seçilen partinin son kullanma tarihi geçmiş.");
            if (requested.RemainingQuantity < quantity)
                return Result<StockBatch?>.Failure(InsufficientStockCode, "Seçilen partide yeterli bakiye yok.");

            return Result<StockBatch?>.Success(requested);
        }

        var candidates = await _batches.ListAsync(
            new StockBatchesWithBalanceForUpdateSpec(tenantId, clinicId, productId), ct);

        var picked = StockBatch.PickFirstSufficient(candidates, quantity, notExpiredOn);
        if (picked is not null)
            return Result<StockBatch?>.Success(picked);

        if (quantityOnHand - candidates.Sum(b => b.RemainingQuantity) >= quantity)
            return Result<StockBatch?>.Success(null);

        // Yalnız süresi geçmiş partilerde yeterli stok varsa nedeni açıkça söyle.
        if (notExpiredOn is not null && StockBatch.PickFirstSufficient(candidates, quantity, notExpiredOn: null) is not null)
            return Result<StockBatch?>.Failure(ExpiredCode, "Yeterli stok yalnız son kullanma tarihi geçmiş partilerde.");

        return Result<StockBatch?>.Failure(InsufficientStockCode, "Yeterli parti bakiyesi yok.");
    }

    /// <summary>Partisiz (eski/devir) bakiye: stok satırındaki toplamdan parti kalanlarının çıkarılmış hâli.</summary>
    public async Task<decimal> GetUnbatchedQuantityAsync(
        Guid tenantId, Guid clinicId, Guid productId, decimal quantityOnHand, CancellationToken ct)
    {
        var candidates = await _batches.ListAsync(
            new StockBatchesWithBalanceForUpdateSpec(tenantId, clinicId, productId), ct);
        return quantityOnHand - candidates.Sum(b => b.RemainingQuantity);
    }

    /// <summary>İzlenen partiyi kiracı/klinik/ürün eşleşmesiyle getirir (ters hareket ve sayım için); yoksa <c>null</c>.</summary>
    public async Task<StockBatch?> FindAsync(
        Guid tenantId, Guid clinicId, Guid productId, Guid batchId, CancellationToken ct)
    {
        var batch = await _batches.FirstOrDefaultAsync(new StockBatchForUpdateSpec(tenantId, batchId), ct);
        return batch is not null && batch.ClinicId == clinicId && batch.ProductId == productId ? batch : null;
    }
}
