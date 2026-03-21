namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

public sealed record ClientDetailDto(
    Guid Id,
    Guid TenantId,
    string FullName,
    string? Phone);
