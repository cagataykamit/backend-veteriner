using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VaccineDefinitions.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Vaccinations;

/// <summary>
/// Aşı kaydı ve aşı takvimi (TIER-005) için seçilen <see cref="VaccineDefinition"/> doğrulaması (aktiflik, kiracı kapsamı, tür uyumu).
/// </summary>
public static class VaccinationCatalogResolver
{
    public static Task<Result<VaccineDefinition>> ResolveActiveForPetAsync(
        Guid tenantId,
        Guid vaccineDefinitionId,
        Pet pet,
        IReadRepository<VaccineDefinition> definitions,
        CancellationToken ct)
        => ResolveActiveForSpeciesAsync(tenantId, vaccineDefinitionId, pet.SpeciesId, definitions, ct);

    public static async Task<Result<VaccineDefinition>> ResolveActiveForSpeciesAsync(
        Guid tenantId,
        Guid vaccineDefinitionId,
        Guid speciesId,
        IReadRepository<VaccineDefinition> definitions,
        CancellationToken ct)
    {
        var def = await definitions.FirstOrDefaultAsync(new VaccineDefinitionByIdSpec(vaccineDefinitionId), ct);
        if (def is null)
            return Result<VaccineDefinition>.Failure("VaccineDefinitions.NotFound", "Aşı tanımı bulunamadı.");

        if (!def.IsActive)
            return Result<VaccineDefinition>.Failure("VaccineDefinitions.Inactive", "Pasif aşı tanımı seçilemez.");

        if (def.TenantId is not null && def.TenantId != tenantId)
            return Result<VaccineDefinition>.Failure("VaccineDefinitions.NotFound", "Aşı tanımı bulunamadı.");

        if (def.SpeciesId is { } sp && sp != speciesId)
        {
            return Result<VaccineDefinition>.Failure(
                "VaccineDefinitions.InvalidSpecies",
                "Seçilen aşı tanımı hayvan türü ile uyumlu değil.");
        }

        return Result<VaccineDefinition>.Success(def);
    }
}
