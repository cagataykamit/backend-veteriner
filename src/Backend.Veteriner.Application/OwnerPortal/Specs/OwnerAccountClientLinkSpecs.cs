using Ardalis.Specification;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal.Specs;

public sealed class OwnerAccountClientLinkByIdSpec : Specification<OwnerAccountClientLink>
{
    public OwnerAccountClientLinkByIdSpec(Guid linkId)
    {
        Query.Where(x => x.Id == linkId);
    }
}

public sealed class OwnerAccountClientLinkByClientAndIdSpec
    : Specification<OwnerAccountClientLink>
{
    public OwnerAccountClientLinkByClientAndIdSpec(Guid tenantId, Guid clientId, Guid linkId)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClientId == clientId && x.Id == linkId);
    }
}

/// <summary>Müşteri kaydının aktif bağı (en fazla bir).</summary>
public sealed class ActiveOwnerAccountClientLinkByClientSpec
    : Specification<OwnerAccountClientLink>
{
    public ActiveOwnerAccountClientLinkByClientSpec(Guid tenantId, Guid clientId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.ClientId == clientId && x.Status == OwnerAccountClientLinkStatus.Active);
    }
}

/// <summary>Müşteri kartı için tüm bağlar (kapalılar dahil), en yeni önce.</summary>
public sealed class OwnerAccountClientLinksByClientSpec : Specification<OwnerAccountClientLink>
{
    public OwnerAccountClientLinksByClientSpec(Guid tenantId, Guid clientId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAtUtc);
    }
}

/// <summary>Hesabın tüm aktif bağları, kiracılar arası (izlenir; hesap silmede kapatılır, ADR-041 K4).</summary>
public sealed class ActiveOwnerAccountClientLinksForCloseByAccountSpec : Specification<OwnerAccountClientLink>
{
    public ActiveOwnerAccountClientLinksForCloseByAccountSpec(Guid ownerAccountId)
    {
        Query.Where(x => x.OwnerAccountId == ownerAccountId && x.Status == OwnerAccountClientLinkStatus.Active);
    }
}

public sealed class ActiveOwnerAccountClientLinksByAccountSpec : Specification<OwnerAccountClientLink>
{
    public ActiveOwnerAccountClientLinksByAccountSpec(Guid ownerAccountId)
    {
        Query.AsNoTracking();
        Query.Where(x => x.OwnerAccountId == ownerAccountId && x.Status == OwnerAccountClientLinkStatus.Active)
            .OrderBy(x => x.CreatedAtUtc);
    }
}
