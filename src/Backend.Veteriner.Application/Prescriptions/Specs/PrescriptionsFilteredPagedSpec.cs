using Ardalis.Specification;
using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Prescriptions;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

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
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        ListSortChoice<PrescriptionListSortField>? sort = null,
        HbsEntryStatus? hbsStatus = null)
    {
        if (hbsStatus is { } hbs)
            Query.Where(HbsTracking.PrescriptionWithStatus(hbs));
        Query.Where(p => p.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
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

        // Tek izinli alan PrescribedAtUtc; sort yoksa azalan (bkz. PrescriptionListSort).
        var ordered = (sort?.Descending ?? true)
            ? Query.OrderByDescending(p => p.PrescribedAtUtc).ThenByDescending(p => p.Id)
            : Query.OrderBy(p => p.PrescribedAtUtc).ThenBy(p => p.Id);

        ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
