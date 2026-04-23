namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>Tenant davet matrisinde tek permission satırı (read-only).</summary>
public sealed record TenantAssignableRolePermissionItemDto(
    string Code,
    string? Description,
    string? Group);
