using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>Şu andan sonra başlayan <see cref="AppointmentStatus.Scheduled"/> randevular (bugünün ileri saatleri dahil).</summary>
public sealed class DashboardUpcomingScheduledCountSpec : Specification<Appointment>
{
    public DashboardUpcomingScheduledCountSpec(Guid tenantId, Guid? clinicId, DateTime fromUtcExclusive)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc > fromUtcExclusive);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
    }
}
