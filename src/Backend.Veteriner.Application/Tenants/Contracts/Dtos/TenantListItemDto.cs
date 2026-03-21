namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

public sealed record TenantListItemDto(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAtUtc);
