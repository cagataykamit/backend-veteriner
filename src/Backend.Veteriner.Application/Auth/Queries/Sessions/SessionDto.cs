namespace Backend.Veteriner.Application.Auth.Queries.Sessions;

public sealed record SessionDto(
    Guid Id,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? RevokedAtUtc,
    string? RevokeReason,
    string? IpAddress,
    string? UserAgent,
    bool IsCurrent
);
