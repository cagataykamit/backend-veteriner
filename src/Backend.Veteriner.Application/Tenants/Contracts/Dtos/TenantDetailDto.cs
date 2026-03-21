namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

public sealed record TenantDetailDto(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAtUtc);
