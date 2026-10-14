using Ardalis.Specification;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.Imports.Specs;

/// <summary>Değer eşleme katalogları: yalnız aktif kayıtlar, salt okunur.</summary>
public sealed class ImportActiveSpeciesSpec : Specification<Species>
{
    public ImportActiveSpeciesSpec()
    {
        Query.AsNoTracking();
        Query.Where(s => s.IsActive);
    }
}

public sealed class ImportActiveBreedsSpec : Specification<Breed>
{
    public ImportActiveBreedsSpec()
    {
        Query.AsNoTracking();
        Query.Where(b => b.IsActive);
    }
}

public sealed class ImportActivePetColorsSpec : Specification<PetColor>
{
    public ImportActivePetColorsSpec()
    {
        Query.AsNoTracking();
        Query.Where(c => c.IsActive);
    }
}

/// <summary>Kiracıya görünür (global + kiracı özel) aktif aşı tanımları.</summary>
public sealed class ImportVisibleVaccineDefinitionsSpec : Specification<VaccineDefinition>
{
    public ImportVisibleVaccineDefinitionsSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(v => v.IsActive && (v.TenantId == null || v.TenantId == tenantId));
    }
}
