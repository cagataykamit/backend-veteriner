using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

/// <summary>
/// Aynı klinikte [startUtc, endUtc) aralığı ile üst üste binen (baş uçlar hariç) başka
/// <see cref="AppointmentStatus.Scheduled"/> randevu var mı.
/// Çakışma: existingStart &lt; newEnd ve newStart &lt; existingEnd.
/// </summary>
public sealed class AppointmentOverlappingAtClinicSpec : Specification<Appointment>
{
    public AppointmentOverlappingAtClinicSpec(
        Guid tenantId,
        Guid clinicId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId = null)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.ClinicId == clinicId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < endUtc
            && startUtc < a.ScheduledAtUtc.AddMinutes(a.DurationMinutes));

        if (excludeAppointmentId is { } excludeId)
            Query.Where(a => a.Id != excludeId);
    }
}
