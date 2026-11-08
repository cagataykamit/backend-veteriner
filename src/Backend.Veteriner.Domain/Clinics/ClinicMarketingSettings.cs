using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Clinics;

/// <summary>
/// Kliniğin ticari ileti kapısı (ADR-046 K2). Kayıt yoksa kapalı (<see cref="IsOpen"/>). Açmak, klinik yöneticisinin İYS beyanını
/// onaylamasını ister; beyan kimin, ne zaman ve hangi metin sürümüyle verdiğiyle tutulur, her açılışta yenilenir. Kapatınca son beyan
/// kayıtta kalır. Hatırlatmalar (randevu, aşı, kontrol, borç) bu kapıdan bağımsızdır.
/// </summary>
public sealed class ClinicMarketingSettings : ITenantOwned
{
    public const string DeclarationRequiredCode = "Reminders.MarketingGate.Validation.DeclarationRequired";

    /// <summary>Panelde onaylanan beyan; metin değişirse <see cref="CurrentDeclarationVersion"/> artar.</summary>
    public const string DeclarationText = "Kliniğimiz İYS'ye kayıtlıdır; onay ve retleri İYS'ye işlemek kliniğin yükümlülüğüdür.";

    public const string CurrentDeclarationVersion = "2026-11-08";

    public const int DeclarationVersionMaxLength = 20;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime? DeclaredAtUtc { get; private set; }
    public Guid? DeclaredByUserId { get; private set; }
    public string? DeclarationVersion { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }

    private ClinicMarketingSettings() { }

    public static ClinicMarketingSettings Create(Guid tenantId, Guid clinicId, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new ClinicMarketingSettings { TenantId = tenantId, ClinicId = clinicId, CreatedAtUtc = utcNow };
    }

    /// <summary>Kayıt yoksa kapalı.</summary>
    public static bool IsOpen(ClinicMarketingSettings? settings) => settings?.IsEnabled == true;

    /// <summary>Kapıyı açar; beyan onaylanmadıysa reddedilir. Her açılış beyanı yeniler.</summary>
    public Result Enable(bool declarationAccepted, Guid userId, DateTime utcNow)
    {
        if (!declarationAccepted)
            return Result.Failure(DeclarationRequiredCode, "Ticari ileti gönderimini açmak için İYS beyanını onaylamalısınız.");

        IsEnabled = true;
        DeclaredAtUtc = utcNow;
        DeclaredByUserId = userId;
        DeclarationVersion = CurrentDeclarationVersion;
        Touch(userId, utcNow);
        return Result.Success();
    }

    public void Disable(Guid userId, DateTime utcNow)
    {
        IsEnabled = false;
        Touch(userId, utcNow);
    }

    private void Touch(Guid userId, DateTime utcNow)
    {
        UpdatedAtUtc = utcNow;
        UpdatedByUserId = userId;
    }
}
