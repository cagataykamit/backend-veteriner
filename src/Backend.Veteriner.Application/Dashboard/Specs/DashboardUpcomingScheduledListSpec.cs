using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

public sealed class DashboardUpcomingScheduledListSpec : Specification<Appointment>
{
    public DashboardUpcomingScheduledListSpec(Guid tenantId, Guid? clinicId, DateTime fromUtcExclusive, int take)
    {
        Query.Where(a =>
                a.TenantId == tenantId
                && a.Status == AppointmentStatus.Scheduled
                && a.ScheduledAtUtc > fromUtcExclusive)
            .Where(a => !clinicId.HasValue || a.ClinicId == clinicId.Value)
            .OrderBy(a => a.ScheduledAtUtc)
            .ThenBy(a => a.Id)
            .Take(take);
    }
}
