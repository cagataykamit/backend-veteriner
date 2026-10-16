using Ardalis.Specification;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal.Specs;

public sealed class OwnerPortalInviteByTokenHashSpec : Specification<OwnerPortalInvite>
{
    public OwnerPortalInviteByTokenHashSpec(string tokenHash)
    {
        Query.Where(x => x.TokenHash == tokenHash);
    }
}

public sealed class OwnerPortalInviteByClientAndIdSpec : Specification<OwnerPortalInvite>
{
    public OwnerPortalInviteByClientAndIdSpec(Guid tenantId, Guid clientId, Guid inviteId)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClientId == clientId && x.Id == inviteId);
    }
}

/// <summary>Müşterinin bekleyen davetleri (izlenir; yeni davet bunları iptal eder).</summary>
public sealed class PendingOwnerPortalInvitesByClientSpec : Specification<OwnerPortalInvite>
{
    public PendingOwnerPortalInvitesByClientSpec(Guid tenantId, Guid clientId)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClientId == clientId && x.Status == OwnerPortalInviteStatus.Pending);
    }
}

/// <summary>Müşteri kartı için son davetler, en yeni önce.</summary>
public sealed class RecentOwnerPortalInvitesByClientSpec : Specification<OwnerPortalInvite>
{
    public RecentOwnerPortalInvitesByClientSpec(Guid tenantId, Guid clientId, int take)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(take);
    }
}
