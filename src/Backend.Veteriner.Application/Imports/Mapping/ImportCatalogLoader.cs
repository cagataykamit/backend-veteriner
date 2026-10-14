using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Catalog;

namespace Backend.Veteriner.Application.Imports.Mapping;

/// <summary>Değer eşleme kataloglarını (tür, ırk, renk, aşı tanımı) okur.</summary>
public sealed class ImportCatalogLoader
{
    private readonly IReadRepository<Species> _species;
    private readonly IReadRepository<Breed> _breeds;
    private readonly IReadRepository<PetColor> _colors;
    private readonly IReadRepository<VaccineDefinition> _vaccines;

    public ImportCatalogLoader(
        IReadRepository<Species> species,
        IReadRepository<Breed> breeds,
        IReadRepository<PetColor> colors,
        IReadRepository<VaccineDefinition> vaccines)
    {
        _species = species;
        _breeds = breeds;
        _colors = colors;
        _vaccines = vaccines;
    }

    public async Task<ImportCatalogs> LoadAsync(Guid tenantId, CancellationToken ct)
    {
        var species = await _species.ListAsync(new ImportActiveSpeciesSpec(), ct);
        var breeds = await _breeds.ListAsync(new ImportActiveBreedsSpec(), ct);
        var colors = await _colors.ListAsync(new ImportActivePetColorsSpec(), ct);
        var vaccines = await _vaccines.ListAsync(new ImportVisibleVaccineDefinitionsSpec(tenantId), ct);

        return new ImportCatalogs(
            species.Select(s => new ImportCatalogEntry(s.Id, s.Name, s.Code, null)).ToList(),
            breeds.Select(b => new ImportCatalogEntry(b.Id, b.Name, null, b.SpeciesId)).ToList(),
            colors.Select(c => new ImportCatalogEntry(c.Id, c.Name, c.Code, null)).ToList(),
            vaccines.Select(v => new ImportCatalogEntry(v.Id, v.Name, v.Code, v.SpeciesId)).ToList());
    }
}
