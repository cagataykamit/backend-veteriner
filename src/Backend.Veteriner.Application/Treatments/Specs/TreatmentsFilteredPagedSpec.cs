using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Treatments;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Treatments.Specs;

public sealed class TreatmentsFilteredPagedSpec : Specification<Treatment, TreatmentListRow>
{
    public TreatmentsFilteredPagedSpec(
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
        ListSortChoice<TreatmentListSortField>? sort = null,
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

        // Tek izinli alan TreatmentDateUtc; sort yoksa azalan (bkz. TreatmentListSort). Aktif süzgeçte sort yoksa en yakın takip önce.
        var ordered = activeFollowUpFromUtc.HasValue && sort is null
            ? Query.OrderBy(t => t.FollowUpDateUtc).ThenBy(t => t.Id)
            : (sort?.Descending ?? true)
                ? Query.OrderByDescending(t => t.TreatmentDateUtc).ThenByDescending(t => t.Id)
                : Query.OrderBy(t => t.TreatmentDateUtc).ThenBy(t => t.Id);

        ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        Query.Select(t => new TreatmentListRow(
            t.Id,
            t.ClinicId,
            t.PetId,
            t.TreatmentDateUtc,
            t.Title,
            t.ExaminationId,
            t.FollowUpDateUtc));
    }
}
