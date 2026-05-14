using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.VaccineDefinitions.Specs;

public sealed class VaccineDefinitionByIdSpec : Specification<VaccineDefinition>
{
    public VaccineDefinitionByIdSpec(Guid id)
    {
        Query.Where(x => x.Id == id);
    }
}

public sealed class VaccineDefinitionsVisibleFilteredCountSpec : Specification<VaccineDefinition>
{
    public VaccineDefinitionsVisibleFilteredCountSpec(
        Guid tenantId,
        bool includeInactive,
        Guid? speciesId,
        string? searchPattern)
    {
        Query.Where(v => v.TenantId == null || v.TenantId == tenantId);

        if (!includeInactive)
            Query.Where(v => v.IsActive);

        if (speciesId.HasValue)
            Query.Where(v => v.SpeciesId == null || v.SpeciesId == speciesId.Value);

        if (searchPattern is not null)
        {
            Query.Where(v =>
                EF.Functions.Like(v.Name, searchPattern)
                || EF.Functions.Like(v.Code, searchPattern));
        }
    }
}

public sealed class VaccineDefinitionsVisibleFilteredPagedSpec : Specification<VaccineDefinition>
{
    public VaccineDefinitionsVisibleFilteredPagedSpec(
        Guid tenantId,
        int page,
        int pageSize,
        bool includeInactive,
        Guid? speciesId,
        string? searchPattern)
    {
        Query.Where(v => v.TenantId == null || v.TenantId == tenantId);

        if (!includeInactive)
            Query.Where(v => v.IsActive);

        if (speciesId.HasValue)
            Query.Where(v => v.SpeciesId == null || v.SpeciesId == speciesId.Value);

        if (searchPattern is not null)
        {
            Query.Where(v =>
                EF.Functions.Like(v.Name, searchPattern)
                || EF.Functions.Like(v.Code, searchPattern));
        }

        Query.OrderBy(v => v.Name).ThenBy(v => v.Code);
        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}

public sealed class VaccineDefinitionTenantCodeExistsSpec : Specification<VaccineDefinition>
{
    public VaccineDefinitionTenantCodeExistsSpec(Guid tenantId, string normalizedCode, Guid? excludeId)
    {
        Query.Where(v => v.TenantId == tenantId && v.Code == normalizedCode);
        if (excludeId.HasValue)
            Query.Where(v => v.Id != excludeId.Value);
    }
}
