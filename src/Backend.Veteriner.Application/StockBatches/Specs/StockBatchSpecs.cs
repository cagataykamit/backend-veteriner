using Ardalis.Specification;
using Backend.Veteriner.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.StockBatches.Specs;

public sealed class StockBatchByIdSpec : Specification<StockBatch>
{
    public StockBatchByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId && b.Id == id);
    }
}

/// <summary>İzlenen (tracked) parti; düşüm/iade/sayım için. Kalan 0 olan parti de gelir.</summary>
public sealed class StockBatchForUpdateSpec : Specification<StockBatch>
{
    public StockBatchForUpdateSpec(Guid tenantId, Guid id)
    {
        Query.Where(b => b.TenantId == tenantId && b.Id == id);
    }
}

public sealed class StockBatchesByIdsSpec : Specification<StockBatch>
{
    public StockBatchesByIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId && ids.Contains(b.Id));
    }
}

/// <summary>Bir klinik+ürünün kalanı olan partileri (izlenen); parti seçimi ve partisiz bakiye hesabı için.</summary>
public sealed class StockBatchesWithBalanceForUpdateSpec : Specification<StockBatch>
{
    public StockBatchesWithBalanceForUpdateSpec(Guid tenantId, Guid clinicId, Guid productId)
    {
        Query.Where(b => b.TenantId == tenantId
            && b.ClinicId == clinicId
            && b.ProductId == productId
            && b.RemainingQuantity > 0);
    }
}

/// <summary>Klinik kapsamlı parti filtresi; sayfalama <see cref="StockBatchesFilteredPagedSpec"/> ile eklenir.</summary>
public class StockBatchesFilteredSpec : Specification<StockBatch>
{
    public StockBatchesFilteredSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? productId,
        string? lotNumberLikePattern,
        bool availableOnly,
        bool? expired,
        DateOnly today,
        DateOnly? expiresBeforeDate)
    {
        Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId && b.ClinicId == clinicId);

        if (productId.HasValue)
            Query.Where(b => b.ProductId == productId.Value);
        if (lotNumberLikePattern is not null)
            Query.Where(b => b.LotNumber != null && EF.Functions.Like(b.LotNumber, lotNumberLikePattern));
        if (availableOnly)
            Query.Where(b => b.RemainingQuantity > 0);
        if (expired == true)
            Query.Where(b => b.ExpiryDate != null && b.ExpiryDate < today);
        else if (expired == false)
            Query.Where(b => b.ExpiryDate == null || b.ExpiryDate >= today);
        if (expiresBeforeDate.HasValue)
            Query.Where(b => b.ExpiryDate != null && b.ExpiryDate <= expiresBeforeDate.Value);
    }
}

/// <summary>FEFO sıralı sayfa: SKT artan (SKT'siz sonda), giriş zamanı, kimlik.</summary>
public sealed class StockBatchesFilteredPagedSpec : StockBatchesFilteredSpec
{
    public StockBatchesFilteredPagedSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? productId,
        string? lotNumberLikePattern,
        bool availableOnly,
        bool? expired,
        DateOnly today,
        DateOnly? expiresBeforeDate,
        int page,
        int pageSize)
        : base(tenantId, clinicId, productId, lotNumberLikePattern, availableOnly, expired, today, expiresBeforeDate)
    {
        Query.OrderBy(b => b.ExpiryDate == null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}

/// <summary>Bir klinik+ürünün kalanı olan partileri FEFO sıralı (okuma); ürün bazlı parti bakiyesi için.</summary>
public sealed class StockBatchesWithBalanceReadSpec : Specification<StockBatch>
{
    public StockBatchesWithBalanceReadSpec(Guid tenantId, Guid clinicId, Guid productId)
    {
        Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId
            && b.ClinicId == clinicId
            && b.ProductId == productId
            && b.RemainingQuantity > 0);
        Query.OrderBy(b => b.ExpiryDate == null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.Id);
    }
}
