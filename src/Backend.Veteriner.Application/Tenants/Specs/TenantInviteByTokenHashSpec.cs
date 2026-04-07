using Ardalis.Specification;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Specs;

public sealed class TenantInviteByTokenHashSpec : Specification<TenantInvite>
{
    public TenantInviteByTokenHashSpec(string tokenHash)
    {
        Query.Where(x => x.TokenHash == tokenHash);
    }
}
