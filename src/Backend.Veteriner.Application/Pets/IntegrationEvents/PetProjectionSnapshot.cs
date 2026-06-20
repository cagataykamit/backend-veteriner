namespace Backend.Veteriner.Application.Pets.IntegrationEvents;

/// <summary>
/// Pet read-model projection için denormalize edilmiş anlık görüntü.
/// Alanlar <c>PetReadModels</c> kolonlarıyla birebir hizalıdır (projection metadata hariç).
/// Normalize değerler command-side desenlerle üretilir:
/// ClientFullNameNormalized = <see cref="Domain.Clients.Client.NormalizeFullNameForDuplicateCheck"/>,
/// Name/Species/Color normalize = trim + invariant lower.
/// </summary>
public sealed record PetProjectionSnapshot(
    Guid PetId,
    Guid TenantId,
    Guid ClientId,
    string ClientFullName,
    string ClientFullNameNormalized,
    string Name,
    string NameNormalized,
    Guid SpeciesId,
    string SpeciesName,
    string SpeciesNameNormalized,
    Guid? BreedId,
    string? Breed,
    string? BreedRefName,
    Guid? ColorId,
    string? ColorName,
    string? ColorNameNormalized,
    int? Gender,
    DateOnly? BirthDate,
    decimal? Weight);
