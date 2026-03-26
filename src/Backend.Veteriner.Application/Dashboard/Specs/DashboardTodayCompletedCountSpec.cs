using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>
/// Planlanma zamanı bugün (UTC) aralığında ve durumu tamamlanmış kayıtlar.
/// Gerçek tamamlanma zamanı alanı olmadığı için iş günü panosu yaklaşımıdır.
/// </summary>
public sealed class DashboardTodayCompletedCountSpec : Specification<Appointment>
{
    public DashboardTodayCompletedCountSpec(Guid tenantId, Guid? clinicId, DateTime dayStartUtc, DateTime dayEndUtc)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Completed
            && a.ScheduledAtUtc >= dayStartUtc
            && a.ScheduledAtUtc < dayEndUtc);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
    }
}
