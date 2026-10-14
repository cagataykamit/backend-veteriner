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

    /// <summary>Testlerde "bugün" = 2026-10-13 (İstanbul günü); süresi geçmiş/geçmemiş ayrımı bu güne göre yapılır.</summary>
    public static readonly DateOnly Today = new(2026, 10, 13);

    public static TimeProvider Clock { get; } = new FixedTimeProvider(new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc));

    /// <summary>Verilen partilerden süresi geçmiş miktarı okuyan okuyucu (varsayılan: hiç parti yok).</summary>
    public static ExpiredStockReader ExpiredReader(params StockBatch[] batches)
    {
        var read = new Mock<IReadRepository<StockBatch>>();
        read
            .Setup(r => r.ListAsync(It.IsAny<ISpecification<StockBatch>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISpecification<StockBatch> s, CancellationToken _) => s.Evaluate(batches).ToList());
        return new(read.Object, Clock);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

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
