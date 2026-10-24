using Ardalis.Specification;
using Backend.Veteriner.Domain.Treatments;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Treatments.Specs;

public sealed class TreatmentsFilteredCountSpec : Specification<Treatment>
{
    public TreatmentsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        DateTime? activeFollowUpFromUtc = null)
    {
        Query.AsNoTracking();
        Query.Where(t => t.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (petId.HasValue)
            Query.Where(t => t.PetId == petId.Value);
        if (dateFromUtc.HasValue)
            Query.Where(t => t.TreatmentDateUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(t => t.TreatmentDateUtc <= dateToUtc.Value);
        if (activeFollowUpFromUtc.HasValue)
            Query.Where(ActiveTreatmentRule.IsActive(activeFollowUpFromUtc.Value));

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(t =>
                EF.Functions.Like(t.Title, pat)
                || EF.Functions.Like(t.Description, pat)
                || (t.Notes != null && EF.Functions.Like(t.Notes, pat))
                || (pids.Length > 0 && pids.Contains(t.PetId)));
        }
    }
}
