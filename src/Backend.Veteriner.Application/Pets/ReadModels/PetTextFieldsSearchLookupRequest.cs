namespace Backend.Veteriner.Application.Pets.ReadModels;

public sealed record PetTextFieldsSearchLookupRequest(
    Guid TenantId,
    string? SearchContainsLikePattern);
