using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class AppointmentsForPetRecentSpec : Specification<Appointment>
{
    public AppointmentsForPetRecentSpec(Guid tenantId, Guid? clinicId, Guid petId, int take)
    {
        Query.Where(a => a.TenantId == tenantId && a.PetId == petId);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        Query.OrderByDescending(a => a.ScheduledAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(take);
    }
}
