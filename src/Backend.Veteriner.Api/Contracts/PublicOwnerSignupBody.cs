using Backend.Veteriner.Application.LegalAcceptances;

namespace Backend.Veteriner.Api.Contracts;

public sealed class PublicOwnerSignupBody
{
    public string PlanCode { get; set; } = default!;
    public string TenantName { get; set; } = default!;
    public string ClinicName { get; set; } = default!;
    public string ClinicCity { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;

    /// <summary>Kabul edilen belgeler ve sürümleri; Kullanım Şartları, Gizlilik ve Veri İşleme'nin güncel sürümü zorunlu (ADR-029).</summary>
    public List<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments { get; set; }

    /// <summary>İsteğe bağlı satış ortağı kodu (ADR-037); geçersizse 400 <c>DiscountCodes.Invalid</c>.</summary>
    public string? DiscountCode { get; set; }

    /// <summary>
    /// Onboarding seçimi (ADR-002): <c>true</c> ise yeni kliniğe Örnek Veteriner Kliniği verisi kayıtla aynı işlemde yüklenir
    /// ve yanıtta <c>exampleClinic</c> özeti döner. Varsayılan <c>false</c> (boş klinik).
    /// </summary>
    public bool StartWithExampleClinic { get; set; }

    /// <summary>Zorunlu; <c>email-code/verify</c> yanıtındaki jeton (TRIAL-001). Yoksa, eşleşmezse ya da süresi dolduysa 400 <c>SignupEmail.VerificationRequired</c>.</summary>
    public string? EmailVerificationToken { get; set; }

    /// <summary>VKN (10 hane) ya da TCKN (11 hane); zorunluluğu <c>GET /public/owner-signup/settings</c> söyler. Aynı numarayla ikinci deneme 409.</summary>
    public string? TaxIdentityNumber { get; set; }
}

/// <summary>Deneme kaydı e-posta kodu isteği (TRIAL_SIGNUP_ABUSE_API_CONTRACT.md §2).</summary>
public sealed class SignupEmailCodeBody
{
    public string? Email { get; set; }

    /// <summary>Cloudflare Turnstile widget jetonu (<c>action</c> = <c>signup-email-code</c>).</summary>
    public string? TurnstileToken { get; set; }
}

/// <summary>Deneme kaydı e-posta kodu doğrulaması (TRIAL_SIGNUP_ABUSE_API_CONTRACT.md §2).</summary>
public sealed class SignupEmailVerifyBody
{
    public string? Email { get; set; }
    public string? OneTimeCode { get; set; }
}
