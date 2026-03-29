namespace Backend.Veteriner.Application.Auth.Commands.Login;

/// <summary>
/// Login/refresh/select-clinic başarı yanıtı.
/// <see cref="TenantMembershipCount"/> yalnızca login cevabında dolu olur (farklı kiracı sayısı; 1 ise girişte tenant seçimi gerekmez).
/// </summary>
public sealed record LoginResultDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    Guid? ResolvedTenantId = null,
    int? TenantMembershipCount = null);