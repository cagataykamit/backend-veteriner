using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Specs;

public sealed class AppointmentsForPetRecentSpec : Specification<Appointment>
{
    public AppointmentsForPetRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(a => a.TenantId == tenantId && a.PetId == petId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderByDescending(a => a.ScheduledAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(take);
    }
}
