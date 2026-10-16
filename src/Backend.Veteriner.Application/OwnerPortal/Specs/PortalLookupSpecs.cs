using Ardalis.Specification;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.OwnerPortal.Specs;

// Portal hesabı kiracılar üstüdür: bağlar (TenantId taşıyan) önce sahiplikle süzülür, ardından bu kimlik listeleriyle ad/durum okunur.

public sealed class PortalTenantsByIdsSpec : Specification<Tenant>
{
    public PortalTenantsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(x => ids.Contains(x.Id));
    }
}

public sealed class PortalClinicsByIdsSpec : Specification<Clinic>
{
    public PortalClinicsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(x => ids.Contains(x.Id));
    }
}

public sealed class PortalClientsByIdsSpec : Specification<Client>
{
    public PortalClientsByIdsSpec(IReadOnlyCollection<Guid> ids)
    {
        Query.AsNoTracking();
        Query.Where(x => ids.Contains(x.Id));
    }
}
