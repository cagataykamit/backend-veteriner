using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Dashboard.Specs;

/// <summary>
/// Belirtilen yarı-açık UTC penceresinde <c>ScheduledAtUtc</c> değerlerini projeksiyonla döner
/// (yalnız zaman damgası; entity takip edilmez). Dashboard 7-günlük randevu trendinde günlük bucket
/// sayımı için kullanılır. **Statü filtresi yoktur**: Scheduled + Completed + Cancelled hepsi dahildir
/// çünkü trend "o gün için planlanmış randevu kayıtları" metriğini taşır (§27.11).
/// </summary>
public sealed class DashboardAppointmentScheduledAtInWindowSpec
    : Specification<Appointment, DateTime>
{
    public DashboardAppointmentScheduledAtInWindowSpec(
        Guid tenantId,
        Guid? clinicId,
        DateTime startUtcInclusive,
        DateTime endUtcExclusive)
    {
        Query.AsNoTracking();
        Query.Where(a =>
            a.TenantId == tenantId
            && a.ScheduledAtUtc >= startUtcInclusive
            && a.ScheduledAtUtc < endUtcExclusive);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        Query.Select(a => a.ScheduledAtUtc);
    }
}
