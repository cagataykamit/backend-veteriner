namespace Backend.Veteriner.Api.Contracts;

public sealed record AuthActionResultDto(
    bool Success,
    string? Message = null);
