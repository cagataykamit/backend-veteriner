namespace Backend.Veteriner.Application.Clients.ReadModels;

public sealed record ClientListReadRequest(
    Guid TenantId,
    int Page,
    int PageSize,
    string? SearchContainsLikePattern);
