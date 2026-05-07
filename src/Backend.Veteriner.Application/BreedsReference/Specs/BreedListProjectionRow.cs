namespace Backend.Veteriner.Application.BreedsReference.Specs;

/// <summary>Irk listesi için Species Include yerine projeksiyon satırı.</summary>
public sealed record BreedListProjectionRow(
    Guid Id,
    Guid SpeciesId,
    string SpeciesName,
    string Name,
    bool IsActive);
