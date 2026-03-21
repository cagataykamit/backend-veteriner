using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

/// <summary>
/// Aynı hayvan aynı anda iki yerde olamaz: aynı pet + aynı zaman için Scheduled kayıt var mı (klinik fark etmeksizin).
/// </summary>
public sealed class AppointmentScheduledSlotForPetSpec : Specification<Appointment>
{
    /// <param name="excludeAppointmentId">Yeniden planlarken mevcut randevuyu hariç tut.</param>
    public AppointmentScheduledSlotForPetSpec(
        Guid tenantId,
        Guid petId,
        DateTime scheduledAtUtc,
        Guid? excludeAppointmentId = null)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.PetId == petId
            && a.ScheduledAtUtc == scheduledAtUtc
            && a.Status == AppointmentStatus.Scheduled);

        if (excludeAppointmentId is { } excludeId)
            Query.Where(a => a.Id != excludeId);
    }
}
