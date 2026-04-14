using Ardalis.Specification;
using Backend.Veteriner.Domain.Examinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Examinations.Specs;

public sealed record ExaminationListRow(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? AppointmentId,
    DateTime ExaminedAtUtc,
    string VisitReason);

public sealed class ExaminationsFilteredPagedSpec : Specification<Examination, ExaminationListRow>
{
    public ExaminationsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        Guid? appointmentId,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
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
        if (appointmentId.HasValue)
            Query.Where(e => e.AppointmentId == appointmentId.Value);
        if (dateFromUtc.HasValue)
            Query.Where(e => e.ExaminedAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(e => e.ExaminedAtUtc <= dateToUtc.Value);

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

        Query.OrderByDescending(e => e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExaminationListRow(
                e.Id,
                e.ClinicId,
                e.PetId,
                e.AppointmentId,
                e.ExaminedAtUtc,
                e.VisitReason));
    }
}
