using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>Planlanma zamanı bugün (UTC) ve iptal edilmiş; iptal anı alanı yoktur.</summary>
public sealed class DashboardTodayCancelledCountSpec : Specification<Appointment>
{
    public DashboardTodayCancelledCountSpec(Guid tenantId, DateTime dayStartUtc, DateTime dayEndUtc)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Cancelled
            && a.ScheduledAtUtc >= dayStartUtc
            && a.ScheduledAtUtc < dayEndUtc);
    }
}
