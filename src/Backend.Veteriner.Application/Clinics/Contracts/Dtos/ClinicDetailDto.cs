namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

public sealed record ClinicDetailDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string City,
    string? District,
    bool IsActive,
    string? Phone,
    string? Email,
    string? Address,
    string? Description);
