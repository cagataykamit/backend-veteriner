using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Reports.Examinations.Specs;

public sealed class ExaminationsReportFilteredPagedSpec : Specification<Examination>
{
    public ExaminationsReportFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        IReadOnlyList<Guid>? restrictedPetIdsForClient,
        Guid? appointmentId,
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int pageSize,
        string? searchContainsLikePattern,
        Guid[] searchPetIds)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(e => e.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(e => e.PetId == petId.Value);
        if (restrictedPetIdsForClient is { Count: > 0 })
            Query.Where(e => restrictedPetIdsForClient.Contains(e.PetId));
        if (appointmentId.HasValue)
            Query.Where(e => e.AppointmentId == appointmentId.Value);

        Query.Where(e => e.ExaminedAtUtc >= fromUtc && e.ExaminedAtUtc <= toUtc);

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

        Query.OrderByDescending(e => e.ExaminedAtUtc).ThenByDescending(e => e.Id);

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
