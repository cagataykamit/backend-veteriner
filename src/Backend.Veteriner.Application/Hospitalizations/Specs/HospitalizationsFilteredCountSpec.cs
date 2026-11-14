using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Hospitalizations.Specs;

public sealed class HospitalizationsFilteredCountSpec : Specification<Hospitalization>
{
    public HospitalizationsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        bool? activeOnly,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        bool includeCancelled = false)
    {
        Query.Where(x => x.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (petId.HasValue)
            Query.Where(x => x.PetId == petId.Value);
        // İptal edilen yatış (ADR-043 AK-5) varsayılan gizli; etkin/geçmiş filtreleri iptali hiç içermez.
        if (activeOnly == true)
            Query.Where(Hospitalization.IsActiveExpression);
        else if (activeOnly == false)
            Query.Where(Hospitalization.IsDischargedExpression);
        else if (!includeCancelled)
            Query.Where(Hospitalization.IsNotCancelledExpression);

        if (dateFromUtc.HasValue)
            Query.Where(x => x.AdmittedAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(x => x.AdmittedAtUtc <= dateToUtc.Value);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(x =>
                EF.Functions.Like(x.Reason, pat)
                || (x.Notes != null && EF.Functions.Like(x.Notes, pat))
                || (pids.Length > 0 && pids.Contains(x.PetId)));
        }
    }
}
