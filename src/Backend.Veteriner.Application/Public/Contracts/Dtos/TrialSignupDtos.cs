namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

/// <param name="ExpiresInSeconds">Kodun geçerlilik süresi.</param>
/// <param name="ResendAfterSeconds">Yeni kod istenebilmesi için bekleme.</param>
public sealed record SignupEmailCodeSentDto(int ExpiresInSeconds, int ResendAfterSeconds);

/// <param name="EmailVerificationToken">Kayıt isteğinde (<c>owner-signup</c>) gönderilecek tek kullanımlık jeton.</param>
/// <param name="ExpiresAtUtc">Jetonun geçerlilik sonu; sonrasında yeni kod istenir.</param>
public sealed record SignupEmailVerifiedDto(string EmailVerificationToken, DateTime ExpiresAtUtc);

/// <param name="TaxIdentityNumberRequired">Kayıtta vergi numarası (VKN/TCKN) zorunlu mu.</param>
/// <param name="TurnstileSiteKey">Cloudflare Turnstile site anahtarı; boşsa widget çizilmez (yerel geliştirme).</param>
public sealed record OwnerSignupSettingsDto(bool TaxIdentityNumberRequired, string? TurnstileSiteKey);
