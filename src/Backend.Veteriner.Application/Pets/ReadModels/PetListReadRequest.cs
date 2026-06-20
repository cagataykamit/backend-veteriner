namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetListReadRequest(
    Guid TenantId,
    int Page,
    int PageSize,
    Guid? ClientId,
    Guid? SpeciesId,
    string? SearchContainsLikePattern);
