using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

public sealed record DashboardUpcomingAppointmentRow(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    DateTime ScheduledAtUtc,
    AppointmentStatus Status);

public sealed class DashboardUpcomingScheduledListSpec : Specification<Appointment, DashboardUpcomingAppointmentRow>
{
    public DashboardUpcomingScheduledListSpec(Guid tenantId, Guid? clinicId, DateTime fromUtcInclusive, int take)
    {
        Query.AsNoTracking();
        Query.Where(a =>
                a.TenantId == tenantId
                && a.Status == AppointmentStatus.Scheduled
                && a.ScheduledAtUtc >= fromUtcInclusive)
            .Where(a => !clinicId.HasValue || a.ClinicId == clinicId.Value)
            .OrderBy(a => a.ScheduledAtUtc)
            .ThenBy(a => a.Id)
            .Take(take)
            .Select(a => new DashboardUpcomingAppointmentRow(
                a.Id,
                a.ClinicId,
                a.PetId,
                a.ScheduledAtUtc,
                a.Status));
    }
}
