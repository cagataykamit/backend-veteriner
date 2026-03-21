using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>
/// Planlanma zamanı bugün (UTC) aralığında ve durumu tamamlanmış kayıtlar.
/// Gerçek tamamlanma zamanı alanı olmadığı için iş günü panosu yaklaşımıdır.
/// </summary>
public sealed class DashboardTodayCompletedCountSpec : Specification<Appointment>
{
    public DashboardTodayCompletedCountSpec(Guid tenantId, DateTime dayStartUtc, DateTime dayEndUtc)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Completed
            && a.ScheduledAtUtc >= dayStartUtc
            && a.ScheduledAtUtc < dayEndUtc);
    }
}
