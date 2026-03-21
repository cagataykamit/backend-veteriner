using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Appointments.Specs;

public sealed class AppointmentByIdSpec : Specification<Appointment>
{
    public AppointmentByIdSpec(Guid tenantId, Guid id)
    {
        Query.Where(a => a.TenantId == tenantId && a.Id == id);
    }
}
