namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

public sealed record ClientDetailDto(
    Guid Id,
    Guid TenantId,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string FullName,
    string? Email,
    string? Phone,
    string? Address);
