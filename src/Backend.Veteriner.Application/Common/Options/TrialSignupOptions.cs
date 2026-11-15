namespace Backend.Veteriner.Application.Common.Options;

/// <summary>
/// Deneme kaydı ayarları (TRIAL-001; sözleşme <c>docs/TRIAL_SIGNUP_ABUSE_API_CONTRACT.md</c>). Kod, süre ve tek deneme kuralları
/// ayar değil, alan kuralıdır (<c>Domain/Tenants</c>).
/// </summary>
public sealed class TrialSignupOptions
{
    public const string SectionName = "TrialSignup";

    /// <summary>
    /// Kayıtta vergi numarası (VKN ya da TCKN) zorunlu mu. Zorunluluğun tek yeri; panel <c>GET /public/owner-signup/settings</c>
    /// ile okur. Kapalıyken girilen numara yine doğrulanır ve tek deneme kuralına girer.
    /// </summary>
    public bool RequireTaxIdentityNumber { get; init; } = true;
}
