namespace Backend.Veteriner.Application.Auth;

/// <summary>
/// Login cold-path için Users tablosundan projection; UserRoles/aggregate yüklenmez.
/// </summary>
public sealed record LoginUserLookupResult(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    string PasswordHash);
