using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed record ClinicNameRow(Guid Id, string Name);

public sealed class ClinicsByTenantIdsNameSpec : Specification<Clinic, ClinicNameRow>
{
    public ClinicsByTenantIdsNameSpec(Guid tenantId, IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && clinicIds.Contains(c.Id))
            .Select(c => new ClinicNameRow(c.Id, c.Name));
    }
}
