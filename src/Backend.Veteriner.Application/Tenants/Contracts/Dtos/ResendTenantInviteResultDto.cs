namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Davet yeniden üretim sonucu. Token alanı yalnızca bir kez (bu yanıtta) görünür; sunucuda hash saklanır.
/// Mevcut davet kaydı güncellenir (Id değişmez). <c>CreatedAtUtc</c> korunur, <c>ExpiresAtUtc</c> yenilenir.
/// </summary>
public sealed record ResendTenantInviteResultDto(
    Guid InviteId,
    string Token,
    string Email,
    Guid TenantId,
    Guid ClinicId,
    DateTime ExpiresAtUtc);
