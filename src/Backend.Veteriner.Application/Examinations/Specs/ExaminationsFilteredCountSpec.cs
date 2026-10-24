using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;
using Microsoft.EntityFrameworkCore;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed class ExaminationsFilteredCountSpec : Specification<Examination>
{
    public ExaminationsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        Guid? appointmentId,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        Guid? visitId = null,
        IReadOnlyCollection<Guid>? clientPetIds = null)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (petId.HasValue)
            Query.Where(e => e.PetId == petId.Value);
        if (appointmentId.HasValue)
            Query.Where(e => e.AppointmentId == appointmentId.Value);
        if (visitId.HasValue)
            Query.Where(e => e.VisitId == visitId.Value);
        if (clientPetIds is not null)
            Query.Where(e => clientPetIds.Contains(e.PetId));
        if (dateFromUtc.HasValue)
            Query.Where(e => e.ExaminedAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(e => e.ExaminedAtUtc < dateToUtc.Value);

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
