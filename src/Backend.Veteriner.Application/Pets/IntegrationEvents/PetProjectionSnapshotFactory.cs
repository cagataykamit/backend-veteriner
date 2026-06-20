using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Pets.IntegrationEvents;

/// <summary>
/// <see cref="Pet"/> aggregate'i ve command handler'da doğrulanmış ilişkili kayıtlardan
/// <see cref="PetProjectionSnapshot"/> üretir.
/// </summary>
public static class PetProjectionSnapshotFactory
{
    public static PetProjectionSnapshot Create(
        Pet pet,
        Client client,
        Species species,
        Breed? breedRef = null,
        PetColor? colorRef = null)
    {
        ArgumentNullException.ThrowIfNull(pet);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(species);

        return new PetProjectionSnapshot(
            pet.Id,
            pet.TenantId,
            pet.ClientId,
            client.FullName,
            Client.NormalizeFullNameForDuplicateCheck(client.FullName),
            pet.Name,
            NormalizeName(pet.Name),
            pet.SpeciesId,
            species.Name,
            NormalizeName(species.Name),
            pet.BreedId,
            pet.Breed,
            breedRef?.Name,
            pet.ColorId,
            colorRef?.Name,
            NormalizeOptionalName(colorRef?.Name),
            pet.Gender is { } gender ? (int)gender : null,
            pet.BirthDate,
            pet.Weight);
    }

    internal static string NormalizeName(string value)
        => value.Trim().ToLowerInvariant();

    internal static string? NormalizeOptionalName(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
