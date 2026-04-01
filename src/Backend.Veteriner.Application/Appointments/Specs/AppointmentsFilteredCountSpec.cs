using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Appointments.Specs;

public sealed class AppointmentsFilteredCountSpec : Specification<Appointment>
{
    public AppointmentsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        AppointmentStatus? status,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds)
    {
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
    }
}
