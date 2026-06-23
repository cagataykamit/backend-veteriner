using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>Şu andan sonra başlayan <see cref="AppointmentStatus.Scheduled"/> randevular (bugünün ileri saatleri dahil).</summary>
public sealed class DashboardUpcomingScheduledCountSpec : Specification<Appointment>
{
    public DashboardUpcomingScheduledCountSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime fromUtcExclusive,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc > fromUtcExclusive);
        DashboardSpecificationClinicScope.ApplyToAppointment(Query, clinicId, accessibleClinicIds);
    }
}
