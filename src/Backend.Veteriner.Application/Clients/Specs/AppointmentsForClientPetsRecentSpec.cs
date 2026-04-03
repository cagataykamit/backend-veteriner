using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class AppointmentsForClientPetsRecentSpec : Specification<Appointment>
{
    public AppointmentsForClientPetsRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid[] petIds,
        int take)
    {
        Query.Where(a => a.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        Query.Where(a => petIds.Contains(a.PetId));
        Query.OrderByDescending(a => a.ScheduledAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(take);
    }
}
