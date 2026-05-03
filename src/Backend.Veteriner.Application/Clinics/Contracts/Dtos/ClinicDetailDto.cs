namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

public sealed record ClinicDetailDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string City,
    bool IsActive,
    string? Phone,
    string? Email,
    string? Address,
    string? Description);
