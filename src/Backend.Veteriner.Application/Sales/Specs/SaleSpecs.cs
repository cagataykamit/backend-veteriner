using Ardalis.Specification;
using Backend.Veteriner.Domain.Sales;

namespace Backend.Veteriner.Application.Sales.Specs;

public sealed class SaleByRequestIdSpec : Specification<Sale>
{
    public SaleByRequestIdSpec(Guid tenantId, Guid requestId)
    {
        Query.AsNoTracking();
        Query.Include(s => s.Lines);
        Query.Where(s => s.TenantId == tenantId && s.RequestId == requestId);
    }
}

public sealed class SaleByIdSpec : Specification<Sale>
{
    public SaleByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Include(s => s.Lines);
        Query.Where(s => s.TenantId == tenantId && s.Id == id);
    }
}

/// <summary>İzlenen (tracked) satış ve satırları; iptal için. İptal edilmiş kayıt da gelir (<c>Sales.AlreadyVoided</c> ayrımı için).</summary>
public sealed class SaleForUpdateSpec : Specification<Sale>
{
    public SaleForUpdateSpec(Guid tenantId, Guid id)
    {
        Query.Include(s => s.Lines);
        Query.Where(s => s.TenantId == tenantId && s.Id == id);
    }
}

/// <summary>Klinik kapsamlı satış filtresi; sayfalama <see cref="SalesFilteredPagedSpec"/> ile eklenir. <paramref name="toUtc"/> hariçtir.</summary>
public class SalesFilteredSpec : Specification<Sale>
{
    public SalesFilteredSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? clientId,
        Guid? productId,
        DateTime? fromUtc,
        DateTime? toUtc,
        bool includeVoided)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId && s.ClinicId == clinicId);
        if (clientId.HasValue)
            Query.Where(s => s.ClientId == clientId.Value);
        if (productId.HasValue)
            Query.Where(s => s.Lines.Any(l => l.ProductId == productId.Value));
        if (fromUtc.HasValue)
            Query.Where(s => s.SoldAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            Query.Where(s => s.SoldAtUtc < toUtc.Value);
        if (!includeVoided)
            Query.Where(s => s.VoidedAtUtc == null);
    }
}

public sealed class SalesFilteredPagedSpec : SalesFilteredSpec
{
    public SalesFilteredPagedSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? clientId,
        Guid? productId,
        DateTime? fromUtc,
        DateTime? toUtc,
        bool includeVoided,
        int page,
        int pageSize)
        : base(tenantId, clinicId, clientId, productId, fromUtc, toUtc, includeVoided)
    {
        Query.Include(s => s.Lines);
        Query.OrderByDescending(s => s.SoldAtUtc)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
