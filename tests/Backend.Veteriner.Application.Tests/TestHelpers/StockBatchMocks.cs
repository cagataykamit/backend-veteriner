using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Domain.Products;
using Moq;

namespace Backend.Veteriner.Application.Tests.TestHelpers;

/// <summary>Parti çözücü için test yardımcıları: varsayılan = hiç parti yok (tüm bakiye partisiz).</summary>
internal static class StockBatchMocks
{
    public static Mock<IRepository<StockBatch>> EmptyRepository() => WithBatches();

    /// <summary>Verilen partileri döndüren depo: spesifikasyonla filtrelenir.</summary>
    public static Mock<IRepository<StockBatch>> WithBatches(params StockBatch[] batches)
    {
        var mock = new Mock<IRepository<StockBatch>>();
        // Spesifikasyon bellekte değerlendirilir (kimlik/kalan filtresi gerçek sorgudaki gibi uygulanır).
        mock
            .Setup(r => r.ListAsync(It.IsAny<ISpecification<StockBatch>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<StockBatch> s, CancellationToken _) => s.Evaluate(batches).ToList());
        mock
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<StockBatch>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<StockBatch> s, CancellationToken _) => s.Evaluate(batches).FirstOrDefault());
        return mock;
    }

    public static StockBatchResolver EmptyResolver() => new(EmptyRepository().Object);

    /// <summary>Test için parti üretir (kalan = ilk miktar).</summary>
    public static StockBatch Batch(
        Guid tenantId,
        Guid clinicId,
        Product product,
        decimal quantity,
        string? lot = null,
        DateOnly? expiry = null,
        DateTime? createdAtUtc = null)
        => StockBatch.Create(
            tenantId,
            clinicId,
            product,
            quantity,
            new StockBatchDetails(LotNumber: lot, ExpiryDate: expiry),
            createdByUserId: null,
            createdAtUtc ?? DateTime.UtcNow).Value!;
}
