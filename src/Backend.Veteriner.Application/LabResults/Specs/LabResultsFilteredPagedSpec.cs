using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.LabResults;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.LabResults.Specs;

public sealed class LabResultsFilteredPagedSpec : Specification<LabResult>
{
    public LabResultsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        int page,
        int pageSize,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        LabResultStatus? status = null,
        ListSortChoice<LabResultListSortField>? sort = null)
    {
        Query.Where(x => x.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (petId.HasValue)
            Query.Where(x => x.PetId == petId.Value);
        if (status.HasValue)
            Query.Where(x => x.Status == status.Value);
        else
            Query.Where(x => x.Status != LabResultStatus.Cancelled);
        if (dateFromUtc.HasValue)
            Query.Where(x => x.RequestedAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(x => x.RequestedAtUtc <= dateToUtc.Value);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(x =>
                EF.Functions.Like(x.TestName, pat)
                || (x.ResultText != null && EF.Functions.Like(x.ResultText, pat))
                || (x.Interpretation != null && EF.Functions.Like(x.Interpretation, pat))
                || (x.Notes != null && EF.Functions.Like(x.Notes, pat))
                || (pids.Length > 0 && pids.Contains(x.PetId)));
        }

        // Tek izinli alan RequestedAtUtc; sort yoksa azalan (bkz. LabResultListSort).
        var ordered = (sort?.Descending ?? true)
            ? Query.OrderByDescending(x => x.RequestedAtUtc).ThenByDescending(x => x.Id)
            : Query.OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id);

        ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
