using Ardalis.Specification;
using Backend.Veteriner.Domain.Hospitalizations;
using Microsoft.EntityFrameworkCore;

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
        Guid[] searchPetIds)
    {
        Query.Where(x => x.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(x => x.ClinicId == clinicId.Value);
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

        Query.OrderByDescending(x => x.AdmittedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
