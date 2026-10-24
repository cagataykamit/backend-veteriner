using Ardalis.Specification;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.CareInstructions.Specs;

internal static class CareInstructionsFilter
{
    public static void Apply(
        ISpecificationBuilder<CareInstruction> query,
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        Guid? petId,
        Guid? hospitalizationId,
        Guid? examinationId,
        Guid? visitId)
    {
        query.Where(x => x.TenantId == tenantId);
        query.ApplyClinicScope(clinicId, accessibleClinicIds);

        if (petId.HasValue)
            query.Where(x => x.PetId == petId.Value);
        if (hospitalizationId.HasValue)
            query.Where(x => x.HospitalizationId == hospitalizationId.Value);
        if (examinationId.HasValue)
            query.Where(x => x.ExaminationId == examinationId.Value);
        if (visitId.HasValue)
            query.Where(x => x.VisitId == visitId.Value);
    }
}

public sealed class CareInstructionsFilteredCountSpec : Specification<CareInstruction>
{
    public CareInstructionsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        Guid? petId,
        Guid? hospitalizationId,
        Guid? examinationId,
        Guid? visitId)
    {
        CareInstructionsFilter.Apply(Query, tenantId, clinicId, accessibleClinicIds, petId, hospitalizationId, examinationId, visitId);
    }
}

public sealed class CareInstructionsFilteredPagedSpec : Specification<CareInstruction>
{
    public CareInstructionsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        Guid? petId,
        Guid? hospitalizationId,
        Guid? examinationId,
        Guid? visitId,
        int page,
        int pageSize)
    {
        CareInstructionsFilter.Apply(Query, tenantId, clinicId, accessibleClinicIds, petId, hospitalizationId, examinationId, visitId);
        Query.OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
