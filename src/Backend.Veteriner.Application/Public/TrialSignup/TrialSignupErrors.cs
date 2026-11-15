using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Public.TrialSignup;

/// <summary>Deneme kaydı kötüye kullanım önlemleri hataları (TRIAL_SIGNUP_ABUSE_API_CONTRACT.md §5).</summary>
public static class TrialSignupErrors
{
    public const string InvalidTaxIdentityMessage =
        "Geçerli bir vergi numarası (10 hane) ya da TC kimlik numarası (11 hane) girin.";

    public static readonly Error DuplicateEmail = new(
        "SignupIdentity.DuplicateEmail",
        "Bu e-posta adresiyle daha önce deneme hesabı açılmış.");

    public static readonly Error DuplicateTaxIdentity = new(
        "SignupIdentity.DuplicateTaxIdentity",
        "Bu vergi numarasıyla daha önce deneme hesabı açılmış.");

    public static readonly Error TaxIdentityRequired = new(
        "SignupIdentity.Validation.TaxIdentityRequired",
        InvalidTaxIdentityMessage);

    public static readonly Error DailyLimitReached = new(
        "SignupEmail.DailyLimitReached",
        "Kod gönderme sınırına ulaşıldı. Lütfen birkaç saat sonra yeniden deneyin.");

    public static readonly Error CodeInvalid = new(
        "SignupEmail.CodeInvalid",
        "Kod hatalı ya da süresi dolmuş. Kodu kontrol edin veya yeni kod isteyin.");

    public static readonly Error VerificationRequired = new(
        "SignupEmail.VerificationRequired",
        "E-posta adresinizi doğrulayın. Doğrulamanın süresi dolduysa yeni kod isteyin.");

    /// <param name="retryAfter">Yeni kod istenebilmesine kalan süre; yanıtta <c>retryAfterSeconds</c> olarak döner.</param>
    public static Error ResendTooSoon(TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        return new Error(
            "SignupEmail.ResendTooSoon",
            $"Yeni kod için {seconds} saniye bekleyin.",
            new Dictionary<string, object?> { ["retryAfterSeconds"] = seconds });
    }
}
