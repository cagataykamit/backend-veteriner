namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetDisplayLookupRequest(
    Guid TenantId,
    IReadOnlyCollection<Guid> PetIds);
