using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>Şu andan sonra başlayan <see cref="AppointmentStatus.Scheduled"/> randevular (bugünün ileri saatleri dahil).</summary>
public sealed class DashboardUpcomingScheduledCountSpec : Specification<Appointment>
{
    public DashboardUpcomingScheduledCountSpec(Guid tenantId, DateTime fromUtcExclusive)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc > fromUtcExclusive);
    }
}
