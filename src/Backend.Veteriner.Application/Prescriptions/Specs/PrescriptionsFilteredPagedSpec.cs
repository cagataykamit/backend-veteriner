using Ardalis.Specification;
using Backend.Veteriner.Domain.Prescriptions;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Prescriptions.Specs;

public sealed class PrescriptionsFilteredPagedSpec : Specification<Prescription>
{
    public PrescriptionsFilteredPagedSpec(
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
        Query.Where(p => p.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(p => p.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(p => p.PetId == petId.Value);
        if (dateFromUtc.HasValue)
            Query.Where(p => p.PrescribedAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(p => p.PrescribedAtUtc <= dateToUtc.Value);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(p =>
                EF.Functions.Like(p.Title, pat)
                || EF.Functions.Like(p.Content, pat)
                || (p.Notes != null && EF.Functions.Like(p.Notes, pat))
                || (pids.Length > 0 && pids.Contains(p.PetId)));
        }

        Query.OrderByDescending(p => p.PrescribedAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
