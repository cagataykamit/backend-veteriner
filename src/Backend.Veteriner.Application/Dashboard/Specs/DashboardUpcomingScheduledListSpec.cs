using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
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
    public DashboardUpcomingScheduledListSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime fromUtcInclusive,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.AsNoTracking();
        Query.Where(a =>
                a.TenantId == tenantId
                && a.Status == AppointmentStatus.Scheduled
                && a.ScheduledAtUtc >= fromUtcInclusive);
        DashboardSpecificationClinicScope.ApplyToAppointment(Query, clinicId, accessibleClinicIds);
        Query
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
