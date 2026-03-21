using Ardalis.Specification;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations.Specs;

public sealed class VaccinationsFilteredCountSpec : Specification<Vaccination>
{
    public VaccinationsFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        VaccinationStatus? status,
        DateTime? dueFromUtc,
        DateTime? dueToUtc,
        DateTime? appliedFromUtc,
        DateTime? appliedToUtc)
    {
        Query.Where(v => v.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(v => v.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(v => v.PetId == petId.Value);
        if (status.HasValue)
            Query.Where(v => v.Status == status.Value);
        if (dueFromUtc.HasValue)
            Query.Where(v => v.DueAtUtc != null && v.DueAtUtc >= dueFromUtc.Value);
        if (dueToUtc.HasValue)
            Query.Where(v => v.DueAtUtc != null && v.DueAtUtc <= dueToUtc.Value);
        if (appliedFromUtc.HasValue)
            Query.Where(v => v.AppliedAtUtc != null && v.AppliedAtUtc >= appliedFromUtc.Value);
        if (appliedToUtc.HasValue)
            Query.Where(v => v.AppliedAtUtc != null && v.AppliedAtUtc <= appliedToUtc.Value);
    }
}
