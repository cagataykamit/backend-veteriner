using Ardalis.Specification;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Visits.Specs;

/// <summary>Hayvanın aktif (tamamlanmamış, yanlış geliş olmayan) gelişi; hayvan başına en çok bir.</summary>
public sealed class ActiveVisitByPetIdSpec : Specification<Visit>
{
    public ActiveVisitByPetIdSpec(Guid tenantId, Guid petId)
    {
        Query.Where(v => v.TenantId == tenantId
                         && v.PetId == petId
                         && v.CareStatus != VisitCareStatus.Completed
                         && v.VoidedAtUtc == null)
            .AsNoTracking();
    }
}
