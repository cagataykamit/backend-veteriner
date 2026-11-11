using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.EDocuments;

public enum EDocumentConnectionStatus
{
    NotConnected = 0,
    Connected = 1,
    AccountSelectionRequired = 2,
    ReauthorizationRequired = 3
}

/// <summary>
/// Kliniğin kendi entegratör hesabı bağlantısı (ADR-036 AK-1 a, ilke 6). Tenant + Clinic benzersiz.
/// <see cref="ProtectedCredentials"/> sağlayıcıya özel gizli verinin (OAuth belirteçleri) şifreli hâlidir; çözmek
/// uygulama katmanının işidir, domain içeriğini bilmez. API yanıtında ve günlükte yer almaz.
/// </summary>
public sealed class EDocumentConnection : ITenantOwned
{
    public const int MaxAccountIdLength = 100;
    public const int MaxAccountNameLength = 300;
    public const int MaxMessageLength = 500;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public EDocumentEnvironment Environment { get; private set; }
    public EDocumentConnectionStatus Status { get; private set; }

    /// <summary>Sağlayıcıdaki hesap / firma kimliği (Paraşüt şirket numarası).</summary>
    public string? AccountId { get; private set; }
    public string? AccountName { get; private set; }

    public string? ProtectedCredentials { get; private set; }

    public Guid? ConnectedByUserId { get; private set; }
    public DateTime? ConnectedAtUtc { get; private set; }
    public DateTime? LastVerifiedAtUtc { get; private set; }
    public bool? LastVerificationOk { get; private set; }
    public string? LastVerificationMessage { get; private set; }
    public DateTime? DisconnectedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Belirteç yenilemeleri eşzamanlı yazılırken kaybolmasın diye.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public bool IsUsable => Status == EDocumentConnectionStatus.Connected && ProtectedCredentials is not null && AccountId is not null;

    private EDocumentConnection() { }

    public static EDocumentConnection Create(Guid tenantId, Guid clinicId, string providerCode, EDocumentEnvironment environment, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (clinicId == Guid.Empty)
            throw new ArgumentException("ClinicId geçersiz.", nameof(clinicId));

        return new EDocumentConnection
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            ProviderCode = providerCode,
            Environment = environment,
            Status = EDocumentConnectionStatus.NotConnected,
            CreatedAtUtc = utcNow,
        };
    }

    /// <summary>Yetkilendirme tamamlandı; hesap belliyse bağlanır, değilse seçim beklenir.</summary>
    public void Authorize(
        string providerCode,
        EDocumentEnvironment environment,
        string protectedCredentials,
        string? accountId,
        string? accountName,
        Guid userId,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(protectedCredentials))
            throw new ArgumentException("Kimlik bilgisi boş olamaz.", nameof(protectedCredentials));

        ProviderCode = providerCode;
        Environment = environment;
        ProtectedCredentials = protectedCredentials;
        AccountId = Clip(accountId, MaxAccountIdLength);
        AccountName = Clip(accountName, MaxAccountNameLength);
        Status = AccountId is null ? EDocumentConnectionStatus.AccountSelectionRequired : EDocumentConnectionStatus.Connected;
        ConnectedByUserId = userId;
        ConnectedAtUtc = utcNow;
        DisconnectedAtUtc = null;
        LastVerifiedAtUtc = null;
        LastVerificationOk = null;
        LastVerificationMessage = null;
        UpdatedAtUtc = utcNow;
    }

    public Result SelectAccount(string accountId, string? accountName, DateTime utcNow)
    {
        if (Status != EDocumentConnectionStatus.AccountSelectionRequired || ProtectedCredentials is null)
            return Result.Failure("EDocuments.InvalidState", "Hesap seçimi beklenmiyor.");
        if (string.IsNullOrWhiteSpace(accountId))
            return Result.Failure("EDocuments.Validation", "Hesap seçilmelidir.");

        AccountId = Clip(accountId, MaxAccountIdLength);
        AccountName = Clip(accountName, MaxAccountNameLength);
        Status = EDocumentConnectionStatus.Connected;
        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>Sağlayıcı yenilenmiş belirteç döndürdü (Paraşüt'te yenileme belirteci her seferinde değişebilir).</summary>
    public void ReplaceCredentials(string protectedCredentials, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(protectedCredentials))
            throw new ArgumentException("Kimlik bilgisi boş olamaz.", nameof(protectedCredentials));
        ProtectedCredentials = protectedCredentials;
        UpdatedAtUtc = utcNow;
    }

    public void RecordVerification(bool ok, string? message, string? accountName, DateTime utcNow)
    {
        LastVerifiedAtUtc = utcNow;
        LastVerificationOk = ok;
        LastVerificationMessage = Clip(message, MaxMessageLength);
        if (accountName is not null)
            AccountName = Clip(accountName, MaxAccountNameLength);
        UpdatedAtUtc = utcNow;
    }

    /// <summary>Sağlayıcı yetkiyi reddetti (yenileme belirteci geçersiz); yeniden bağlanmak gerekir.</summary>
    public void RequireReauthorization(string? message, DateTime utcNow)
    {
        Status = EDocumentConnectionStatus.ReauthorizationRequired;
        ProtectedCredentials = null;
        RecordVerification(false, message, null, utcNow);
    }

    public void Disconnect(DateTime utcNow)
    {
        Status = EDocumentConnectionStatus.NotConnected;
        ProtectedCredentials = null;
        AccountId = null;
        AccountName = null;
        DisconnectedAtUtc = utcNow;
        LastVerifiedAtUtc = null;
        LastVerificationOk = null;
        LastVerificationMessage = null;
        UpdatedAtUtc = utcNow;
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length > max ? text[..max] : text;
    }
}
