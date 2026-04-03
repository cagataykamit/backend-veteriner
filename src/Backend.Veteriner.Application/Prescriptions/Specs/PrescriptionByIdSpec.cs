using Ardalis.Specification;
using Backend.Veteriner.Domain.Prescriptions;

namespace Backend.Veteriner.Application.Prescriptions.Specs;

public sealed class PrescriptionByIdSpec : Specification<Prescription>
{
    public PrescriptionByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(p => p.TenantId == tenantId && p.Id == id);
    }
}
