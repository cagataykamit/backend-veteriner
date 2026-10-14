using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>
/// Parti listesi filtresi. Klinik bağsız parti kiracı genelidir; klinik bağlı parti yalnız erişilebilen kliniklerde görünür.
/// <paramref name="singleClinicId"/> ve <paramref name="accessibleClinicIds"/> ikisi de null ise klinik filtresi yoktur.
/// </summary>
public sealed class ImportBatchesFilteredSpec : Specification<ImportBatch>
{
    public ImportBatchesFilteredSpec(
        Guid tenantId,
        ImportBatchStatus? status,
        Guid? singleClinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        int? page = null,
        int? pageSize = null)
    {
        Query.AsNoTracking();
        Query.Where(b => b.TenantId == tenantId);

        if (status.HasValue)
            Query.Where(b => b.Status == status.Value);

        if (singleClinicId is { } clinicId)
        {
            Query.Where(b => b.ClinicId == null || b.ClinicId == clinicId);
        }
        else if (accessibleClinicIds is not null)
        {
            var ids = accessibleClinicIds.ToArray();
            Query.Where(b => b.ClinicId == null || ids.Contains(b.ClinicId.Value));
        }

        if (page is { } p && pageSize is { } size)
        {
            Query.OrderByDescending(b => b.CreatedAtUtc)
                .ThenByDescending(b => b.Id)
                .Skip((p - 1) * size)
                .Take(size);
        }
    }
}
