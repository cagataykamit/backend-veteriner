using Ardalis.Specification;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Charges.Specs;

/// <summary>Klinik kapsamlı ücret filtresi; sayfalama <see cref="ChargesFilteredPagedSpec"/> ile eklenir.</summary>
public class ChargesFilteredSpec : Specification<Charge>
{
    public ChargesFilteredSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? clientId,
        Guid? petId,
        Guid? visitId,
        Guid? examinationId)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId);
        if (clientId.HasValue)
            Query.Where(c => c.ClientId == clientId.Value);
        if (petId.HasValue)
            Query.Where(c => c.PetId == petId.Value);
        if (visitId.HasValue)
            Query.Where(c => c.VisitId == visitId.Value);
        if (examinationId.HasValue)
            Query.Where(c => c.ExaminationId == examinationId.Value);
    }
}

public sealed class ChargesFilteredPagedSpec : ChargesFilteredSpec
{
    public ChargesFilteredPagedSpec(
        Guid tenantId,
        Guid clinicId,
        Guid? clientId,
        Guid? petId,
        Guid? visitId,
        Guid? examinationId,
        int page,
        int pageSize)
        : base(tenantId, clinicId, clientId, petId, visitId, examinationId)
    {
        Query.OrderByDescending(c => c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
