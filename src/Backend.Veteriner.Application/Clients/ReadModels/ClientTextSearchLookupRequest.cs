namespace Backend.Veteriner.Application.Clients.ReadModels;

public sealed record ClientTextSearchLookupRequest(
    Guid TenantId,
    string? SearchContainsLikePattern);
