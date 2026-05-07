namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>Pet liste endpoint’i için Include yerine tek SELECT projeksiyonu.</summary>
public sealed record PetListProjectionRow(
    Guid Id,
    Guid TenantId,
    Guid ClientId,
    string Name,
    Guid SpeciesId,
    string SpeciesName,
    Guid? ColorId,
    string? ColorName,
    string? Breed,
    decimal? Weight);
