using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

/// <summary>
/// Aynı klinikte aynı UTC zaman damgasında halen <see cref="AppointmentStatus.Scheduled"/> olan başka randevu var mı.
/// </summary>
public sealed class AppointmentScheduledSlotAtClinicSpec : Specification<Appointment>
{
    /// <param name="excludeAppointmentId">Yeniden planlarken mevcut randevuyu hariç tut.</param>
    public AppointmentScheduledSlotAtClinicSpec(
        Guid tenantId,
        Guid clinicId,
        DateTime scheduledAtUtc,
        Guid? excludeAppointmentId = null)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.ClinicId == clinicId
            && a.ScheduledAtUtc == scheduledAtUtc
            && a.Status == AppointmentStatus.Scheduled);

        if (excludeAppointmentId is { } excludeId)
            Query.Where(a => a.Id != excludeId);
    }
}
