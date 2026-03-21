namespace Backend.Veteriner.Application.Users.Contracts.Dtos;

/// <summary>
/// Admin kullanıcı detay.
/// </summary>
public sealed record AdminUserDetailDto(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Roles
);
