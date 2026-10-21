using Ardalis.Specification;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics.Specs;

public sealed class ClinicTaxpayerSettingsByClinicSpec : Specification<ClinicTaxpayerSettings>
{
    public ClinicTaxpayerSettingsByClinicSpec(Guid tenantId, Guid clinicId)
    {
        Query.Where(x => x.TenantId == tenantId && x.ClinicId == clinicId);
    }
}
