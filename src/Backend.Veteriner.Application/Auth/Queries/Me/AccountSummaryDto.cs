namespace Backend.Veteriner.Application.Auth.Queries.Me;

/// <summary>
/// Oturum açmış kullanıcının hesap özeti (read-only).
/// Hassas alanlar (şifre hash, refresh token, security stamp vb.) bilerek dahil edilmez.
/// </summary>
public sealed record AccountSummaryDto(
    Guid UserId,
    string Email,
    string? FirstName,
    string? LastName,
    string? DisplayName,
    Guid? TenantId,
    string? TenantName,
    Guid? ActiveClinicId,
    string? ActiveClinicName,
    IReadOnlyList<string> Roles,
    bool IsTenantWide);
