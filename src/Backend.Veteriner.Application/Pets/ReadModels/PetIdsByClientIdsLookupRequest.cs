namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetIdsByClientIdsLookupRequest(
    Guid TenantId,
    IReadOnlyCollection<Guid> ClientIds);
