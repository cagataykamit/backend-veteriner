using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Reports.Examinations.Specs;

/// <summary>
/// <see cref="Examination.ExaminedAtUtc"/> üzerinde yarı-açık aralık <c>[fromUtc, toUtc)</c> (UTC).
/// Liste endpoint’iyle aynı arama alanları.</summary>
public sealed class ExaminationsReportFilteredCountSpec : Specification<Examination>
{
    public ExaminationsReportFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        IReadOnlyList<Guid>? restrictedPetIdsForClient,
        Guid? appointmentId,
        DateTime fromUtc,
        DateTime toUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (petId.HasValue)
            Query.Where(e => e.PetId == petId.Value);
        if (restrictedPetIdsForClient is { Count: > 0 })
            Query.Where(e => restrictedPetIdsForClient.Contains(e.PetId));
        if (appointmentId.HasValue)
            Query.Where(e => e.AppointmentId == appointmentId.Value);

        Query.Where(e => e.ExaminedAtUtc >= fromUtc && e.ExaminedAtUtc < toUtc);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(e =>
                EF.Functions.Like(e.VisitReason, pat)
                || EF.Functions.Like(e.Findings, pat)
                || (e.Assessment != null && EF.Functions.Like(e.Assessment, pat))
                || (e.Notes != null && EF.Functions.Like(e.Notes, pat))
                || (pids.Length > 0 && pids.Contains(e.PetId)));
        }
    }
}
