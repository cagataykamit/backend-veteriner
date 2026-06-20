namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetTextSearchLookupRequest(
    Guid TenantId,
    string? SearchContainsLikePattern);
