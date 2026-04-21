using Ardalis.Specification;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Reports.Vaccinations.Specs;

public sealed class VaccinationsReportFilteredOrderedForExportSpec : Specification<Vaccination>
{
    public VaccinationsReportFilteredOrderedForExportSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        IReadOnlyList<Guid>? restrictedPetIdsForClient,
        VaccinationStatus? status,
        DateTime fromUtc,
        DateTime toUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(v => v.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(v => v.PetId == petId.Value);
        if (restrictedPetIdsForClient is { Count: > 0 })
            Query.Where(v => restrictedPetIdsForClient.Contains(v.PetId));
        if (status.HasValue)
            Query.Where(v => v.Status == status.Value);

        Query.Where(v =>
            (v.AppliedAtUtc != null
                && v.AppliedAtUtc >= fromUtc
                && v.AppliedAtUtc <= toUtc)
            || (v.AppliedAtUtc == null
                && v.DueAtUtc != null
                && v.DueAtUtc >= fromUtc
                && v.DueAtUtc <= toUtc));

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(v =>
                EF.Functions.Like(v.VaccineName, pat)
                || (v.Notes != null && EF.Functions.Like(v.Notes, pat))
                || (pids.Length > 0 && pids.Contains(v.PetId)));
        }

        Query.OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc).ThenByDescending(v => v.Id);
    }
}
