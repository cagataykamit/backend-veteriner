using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicTaxpayerSettingsByClinicIdsSpec : Specification<ClinicTaxpayerSettings>
{
    public ClinicTaxpayerSettingsByClinicIdsSpec(Guid tenantId, IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(x => x.TenantId == tenantId && clinicIds.Contains(x.ClinicId));
    }
}
