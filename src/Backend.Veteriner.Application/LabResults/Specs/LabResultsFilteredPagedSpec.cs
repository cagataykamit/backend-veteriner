using Ardalis.Specification;
using Backend.Veteriner.Domain.LabResults;
using Microsoft.EntityFrameworkCore;

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
        Guid[] searchPetIds)
    {
        Query.Where(x => x.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(x => x.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(x => x.PetId == petId.Value);
        if (dateFromUtc.HasValue)
            Query.Where(x => x.ResultDateUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(x => x.ResultDateUtc <= dateToUtc.Value);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(x =>
                EF.Functions.Like(x.TestName, pat)
                || EF.Functions.Like(x.ResultText, pat)
                || (x.Interpretation != null && EF.Functions.Like(x.Interpretation, pat))
                || (x.Notes != null && EF.Functions.Like(x.Notes, pat))
                || (pids.Length > 0 && pids.Contains(x.PetId)));
        }

        Query.OrderByDescending(x => x.ResultDateUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
