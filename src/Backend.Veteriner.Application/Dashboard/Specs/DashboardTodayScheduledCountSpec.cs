using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>Bugün (UTC) için planlanmış ve hâlâ <see cref="AppointmentStatus.Scheduled"/> olan randevular.</summary>
public sealed class DashboardTodayScheduledCountSpec : Specification<Appointment>
{
    public DashboardTodayScheduledCountSpec(Guid tenantId, Guid? clinicId, DateTime dayStartUtc, DateTime dayEndUtc)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc >= dayStartUtc
            && a.ScheduledAtUtc < dayEndUtc);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
    }
}
