namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

public sealed record ClientListItemDto(
    Guid Id,
    Guid TenantId,
    DateTime CreatedAtUtc,
    string FullName,
    string? Email,
    string? Phone);
