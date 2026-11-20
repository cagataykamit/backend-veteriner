using Ardalis.Specification;
using Backend.Veteriner.Domain.VitalMeasurements;

namespace Backend.Veteriner.Application.VitalMeasurements.Specs;

public sealed class VitalMeasurementByIdSpec : Specification<VitalMeasurement>
{
    public VitalMeasurementByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(x => x.TenantId == tenantId && x.Id == id);
    }
}
