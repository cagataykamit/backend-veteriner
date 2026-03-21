namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

public sealed record ClinicDetailDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string City,
    bool IsActive);
