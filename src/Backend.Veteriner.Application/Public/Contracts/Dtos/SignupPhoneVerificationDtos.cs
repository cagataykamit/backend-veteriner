namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

/// <param name="ExpiresInSeconds">Kodun geçerlilik süresi.</param>
/// <param name="ResendAfterSeconds">Yeni kod istenebilmesi için bekleme.</param>
public sealed record SignupPhoneCodeSentDto(int ExpiresInSeconds, int ResendAfterSeconds);

/// <param name="PhoneVerificationToken">Kayıt isteğinde (<c>owner-signup</c>) gönderilecek tek kullanımlık jeton.</param>
/// <param name="ExpiresAtUtc">Jetonun geçerlilik sonu; sonrasında yeni kod istenir.</param>
public sealed record SignupPhoneVerifiedDto(string PhoneVerificationToken, DateTime ExpiresAtUtc);
