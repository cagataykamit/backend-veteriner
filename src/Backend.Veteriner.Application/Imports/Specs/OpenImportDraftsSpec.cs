using Ardalis.Specification;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>Kiracının açık taslak partileri (sınır denetimi için sayılır).</summary>
public sealed class OpenImportDraftsSpec : Specification<ImportBatch>
{
    public OpenImportDraftsSpec(Guid tenantId)
    {
        var draft = ImportBatchStatusSets.Draft.ToArray();
        Query.Where(b => b.TenantId == tenantId && draft.Contains(b.Status));
    }
}
