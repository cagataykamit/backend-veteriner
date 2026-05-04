using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

/// <summary>
/// Aynı hayvan için zaman aralığı çakışması: başka klinikte olsa bile pet tek aktif randevuda olmalı.
/// </summary>
public sealed class AppointmentOverlappingForPetSpec : Specification<Appointment>
{
    public AppointmentOverlappingForPetSpec(
        Guid tenantId,
        Guid petId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId = null)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.PetId == petId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < endUtc
            && startUtc < a.ScheduledAtUtc.AddMinutes(a.DurationMinutes));

        if (excludeAppointmentId is { } excludeId)
            Query.Where(a => a.Id != excludeId);
    }
}
