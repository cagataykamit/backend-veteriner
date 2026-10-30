using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

/// <summary>
/// Aynı hekim için zaman aralığı çakışması (ADR-042 K3): hekimin planlı randevuları üst üste binemez.
/// Hekimi olmayan randevular bu kurala girmez.
/// </summary>
public sealed class AppointmentOverlappingForVeterinarianSpec : Specification<Appointment>
{
    public AppointmentOverlappingForVeterinarianSpec(
        Guid tenantId,
        Guid veterinarianUserId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId = null)
    {
        Query.Where(a =>
            a.TenantId == tenantId
            && a.VeterinarianUserId == veterinarianUserId
            && a.Status == AppointmentStatus.Scheduled
            && a.ScheduledAtUtc < endUtc
            && startUtc < a.ScheduledAtUtc.AddMinutes(a.DurationMinutes));

        if (excludeAppointmentId is { } excludeId)
            Query.Where(a => a.Id != excludeId);
    }
}
