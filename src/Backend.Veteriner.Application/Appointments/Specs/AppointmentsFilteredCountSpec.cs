using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

public sealed class AppointmentsFilteredCountSpec : Specification<Appointment>
{
    public AppointmentsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        AppointmentStatus? status,
        DateTime? dateFromUtc,
        DateTime? dateToUtc)
    {
        Query.Where(a => a.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(a => a.PetId == petId.Value);
        if (status.HasValue)
            Query.Where(a => a.Status == status.Value);
        if (dateFromUtc.HasValue)
            Query.Where(a => a.ScheduledAtUtc >= dateFromUtc.Value);
        if (dateToUtc.HasValue)
            Query.Where(a => a.ScheduledAtUtc <= dateToUtc.Value);
    }
}
