namespace Backend.Veteriner.Application.Users.Contracts.Dtos;

/// <summary>
/// Admin kullanıcı liste satırı.
/// </summary>
public sealed record AdminUserListItemDto(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Roles
);
