using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Specs;

public sealed class AppointmentsFilteredPagedSpec : Specification<Appointment>
{
    public AppointmentsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        AppointmentStatus? status,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        int page,
        int pageSize,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        bool scheduledAtDescending)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(a => a.PetId == petId.Value);
        if (status.HasValue)
            Query.Where(a => a.Status == status.Value);
        if (dateFromUtc.HasValue)
            Query.Where(a => a.ScheduledAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(a => a.ScheduledAtUtc <= dateToUtc.Value);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(a =>
                (a.Notes != null && EF.Functions.Like(a.Notes, pat))
                || (pids.Length > 0 && pids.Contains(a.PetId)));
        }

        if (scheduledAtDescending)
            Query.OrderByDescending(a => a.ScheduledAtUtc).ThenByDescending(a => a.Id);
        else
            Query.OrderBy(a => a.ScheduledAtUtc).ThenBy(a => a.Id);

        Query.Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
