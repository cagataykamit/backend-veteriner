namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>POST /clients yanıt gövdesi; frontend yönlendirme için <see cref="Id"/> her zaman dolu.</summary>
public sealed record ClientCreatedDto(
    Guid Id,
    Guid TenantId,
    string FullName,
    string? Email,
    string? Phone);
