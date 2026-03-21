using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

public sealed class AppointmentsFilteredPagedSpec : Specification<Appointment>
{
    public AppointmentsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        AppointmentStatus? status,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        int page,
        int pageSize)
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

        Query.OrderBy(a => a.ScheduledAtUtc)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
