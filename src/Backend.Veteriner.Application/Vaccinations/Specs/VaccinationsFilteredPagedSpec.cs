using Ardalis.Specification;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Vaccinations.Specs;

public sealed class VaccinationsFilteredPagedSpec : Specification<Vaccination>
{
    public VaccinationsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        VaccinationStatus? status,
        DateTime? dueFromUtc,
        DateTime? dueToUtc,
        DateTime? appliedFromUtc,
        DateTime? appliedToUtc,
        int page,
        int pageSize,
        string? searchContainsLikePattern,
        Guid[] searchPetIds)
    {
        Query.AsNoTracking();
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

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(v =>
                EF.Functions.Like(v.VaccineName, pat)
                || (v.Notes != null && EF.Functions.Like(v.Notes, pat))
                || (pids.Length > 0 && pids.Contains(v.PetId)));
        }

        // Önce gerçekleşen / planlanan zamana göre (uygulama varsa o, yoksa vade); sonra Id.
        Query.OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc)
            .ThenByDescending(v => v.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
