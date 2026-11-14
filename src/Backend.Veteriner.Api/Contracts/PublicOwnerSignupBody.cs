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

    /// <summary>Zorunlu cep telefonu (TRIAL-001); <c>phone-code/verify</c> ile doğrulanan numarayla aynı olmalı.</summary>
    public string? MobilePhone { get; set; }

    /// <summary>Kod doğrulamasından dönen tek kullanımlık jeton; yoksa, eşleşmezse ya da süresi dolduysa 400 <c>SignupPhone.VerificationRequired</c>.</summary>
    public string? PhoneVerificationToken { get; set; }
}

/// <summary>Deneme kaydı telefon kodu isteği (TRIAL_PHONE_VERIFICATION_API_CONTRACT.md §2).</summary>
public sealed class SignupPhoneCodeBody
{
    public string? MobilePhone { get; set; }
}

/// <summary>Deneme kaydı telefon kodu doğrulaması (TRIAL_PHONE_VERIFICATION_API_CONTRACT.md §3).</summary>
public sealed class SignupPhoneVerifyBody
{
    public string? MobilePhone { get; set; }
    public string? OneTimeCode { get; set; }
}
