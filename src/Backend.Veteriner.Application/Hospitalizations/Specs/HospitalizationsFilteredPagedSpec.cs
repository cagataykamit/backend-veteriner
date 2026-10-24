using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Hospitalizations;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Hospitalizations.Specs;

public sealed class HospitalizationsFilteredPagedSpec : Specification<Hospitalization>
{
    public HospitalizationsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        bool? activeOnly,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        int page,
        int pageSize,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        ListSortChoice<HospitalizationListSortField>? sort = null)
    {
        Query.Where(x => x.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (petId.HasValue)
            Query.Where(x => x.PetId == petId.Value);
        if (activeOnly == true)
            Query.Where(x => x.DischargedAtUtc == null);
        else if (activeOnly == false)
            Query.Where(x => x.DischargedAtUtc != null);

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

        // Tek izinli alan AdmittedAtUtc; sort yoksa azalan (bkz. HospitalizationListSort).
        var ordered = (sort?.Descending ?? true)
            ? Query.OrderByDescending(x => x.AdmittedAtUtc).ThenByDescending(x => x.Id)
            : Query.OrderBy(x => x.AdmittedAtUtc).ThenBy(x => x.Id);

        ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
