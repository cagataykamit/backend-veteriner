using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Clients.Specs;

public sealed class AppointmentsForClientPetsRecentSpec : Specification<Appointment>
{
    public AppointmentsForClientPetsRecentSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid[] petIds,
        int take,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null)
    {
        Query.Where(a => a.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.Where(a => petIds.Contains(a.PetId));
        Query.OrderByDescending(a => a.ScheduledAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(take);
    }
}
