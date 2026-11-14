using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Public.PhoneVerification;

/// <summary>Deneme kaydı telefon doğrulaması hataları (TRIAL_PHONE_VERIFICATION_API_CONTRACT.md §4).</summary>
public static class SignupPhoneErrors
{
    public const string InvalidPhoneMessage = "Geçerli bir cep telefonu numarası girin (5XX XXX XX XX).";

    public static readonly Error DuplicateTrial = new(
        "SignupPhone.DuplicateTrial",
        "Bu telefon numarasıyla daha önce deneme hesabı açılmış.");

    public static readonly Error DailyLimitReached = new(
        "SignupPhone.DailyLimitReached",
        "Kod gönderme sınırına ulaşıldı. Lütfen birkaç saat sonra yeniden deneyin.");

    public static readonly Error CodeInvalid = new(
        "SignupPhone.CodeInvalid",
        "Kod hatalı ya da süresi dolmuş. Kodu kontrol edin veya yeni kod isteyin.");

    public static readonly Error VerificationRequired = new(
        "SignupPhone.VerificationRequired",
        "Telefon numaranızı doğrulayın. Doğrulamanın süresi dolduysa yeni kod isteyin.");

    public static readonly Error SmsNotConfigured = new(
        "Sms.NotConfigured",
        "Telefon doğrulaması şu an kullanılamıyor. Lütfen daha sonra yeniden deneyin.");

    public static readonly Error SmsSendFailed = new(
        "Sms.SendFailed",
        "Kod gönderilemedi. Lütfen biraz sonra yeniden deneyin.");

    /// <param name="retryAfter">Yeni kod istenebilmesine kalan süre; yanıtta <c>retryAfterSeconds</c> olarak döner.</param>
    public static Error ResendTooSoon(TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        return new Error(
            "SignupPhone.ResendTooSoon",
            $"Yeni kod için {seconds} saniye bekleyin.",
            new Dictionary<string, object?> { ["retryAfterSeconds"] = seconds });
    }
}
