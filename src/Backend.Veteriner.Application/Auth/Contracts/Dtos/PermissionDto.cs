namespace Backend.Veteriner.Application.Auth.Contracts.Dtos;

public sealed record PermissionDto(
    Guid Id,
    string Code,
    string? Description
);
