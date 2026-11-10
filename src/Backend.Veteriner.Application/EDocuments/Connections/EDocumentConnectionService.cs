using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Application.EDocuments.Connections;

/// <summary>
/// Bağlantı yüzeyinin ortak parçaları: DTO üretimi (hesap seçimi bekleniyorsa seçenekler sağlayıcıdan okunur), bağlantı
/// sınaması ve OAuth state / PKCE üretimi. State sunucu tarafında saklanmaz; Data Protection ile şifreli ve süreli taşınır.
/// </summary>
public sealed class EDocumentConnectionService
{
    public const string StatePurpose = "EDocuments.OAuthState.v1";
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly IEDocumentProviderResolver _providers;
    private readonly EDocumentProviderGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _timeProvider;

    public EDocumentConnectionService(
        IEDocumentProviderResolver providers,
        EDocumentProviderGateway gateway,
        ISecretProtector protector,
        TimeProvider timeProvider)
    {
        _providers = providers;
        _gateway = gateway;
        _protector = protector;
        _timeProvider = timeProvider;
    }

    public async Task<EDocumentConnectionDto> ToDtoAsync(Guid clinicId, EDocumentConnection? connection, CancellationToken ct)
    {
        IReadOnlyList<EDocumentAccountChoiceDto>? choices = null;
        if (connection is { Status: EDocumentConnectionStatus.AccountSelectionRequired })
        {
            var accounts = await ListAccountsAsync(connection, ct);
            choices = accounts?.Select(a => new EDocumentAccountChoiceDto(a.AccountId, a.Name, a.TaxNumber)).ToList();
        }

        var available = _providers.All
            .Select(p => new EDocumentProviderOptionDto(p.ProviderCode, p.DisplayName, p.IsConfigured))
            .ToList();

        if (connection is null || connection.Status == EDocumentConnectionStatus.NotConnected)
        {
            return new EDocumentConnectionDto(
                clinicId, connection?.ProviderCode, ProviderName(connection?.ProviderCode), null,
                EDocumentConnectionStatus.NotConnected, null, null, null, null, null, null, null, available);
        }

        return new EDocumentConnectionDto(
            clinicId,
            connection.ProviderCode,
            ProviderName(connection.ProviderCode),
            connection.Environment,
            connection.Status,
            connection.AccountId,
            connection.AccountName,
            connection.ConnectedAtUtc,
            connection.LastVerifiedAtUtc,
            connection.LastVerificationOk,
            connection.LastVerificationMessage,
            choices,
            available);
    }

    /// <summary>Hesap seçimi bekleyen bağlantının erişebildiği hesaplar; okunamazsa <c>null</c>.</summary>
    public async Task<IReadOnlyList<ProviderAccountInfo>?> ListAccountsAsync(EDocumentConnection connection, CancellationToken ct)
    {
        var oauth = _providers.FindOAuth(connection.ProviderCode);
        if (oauth is null || connection.ProtectedCredentials is null)
            return null;
        var credentials = _gateway.UnprotectCredentials(connection.ProtectedCredentials);
        if (credentials is null)
            return null;

        var result = await oauth.ListAccountsAsync(credentials, ct);
        _gateway.Absorb(connection, result);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>Bağlantıyı sınar ve sonucu bağlantıya yazar (kayıt çağıranda).</summary>
    public async Task VerifyAsync(EDocumentConnection connection, CancellationToken ct)
    {
        var opened = _gateway.Open(connection);
        if (!opened.IsSuccess)
        {
            if (connection.Status == EDocumentConnectionStatus.Connected)
                connection.RecordVerification(false, opened.Error.Message, null, Now());
            return;
        }

        var session = opened.Value!;
        var result = await session.Provider.VerifyAsync(session.Account, ct);
        _gateway.Absorb(connection, result);
        if (!result.IsSuccess)
        {
            if (connection.Status == EDocumentConnectionStatus.Connected)
                connection.RecordVerification(false, result.Error!.Message, null, Now());
            return;
        }

        var check = result.Value!;
        var message = check.EDocumentsEnabled == false
            ? "Bağlantı çalışıyor; ancak hesapta e-belge kullanımı açık görünmüyor. Entegratör ekranından e-belgeyi etkinleştirin."
            : "Bağlantı çalışıyor.";
        connection.RecordVerification(true, message, check.AccountName, Now());
    }

    public string CreateState(EDocumentAuthorizationState state)
        => _protector.ProtectTimeLimited(StatePurpose, JsonSerializer.Serialize(state), StateLifetime);

    public EDocumentAuthorizationState? ReadState(string? protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState))
            return null;
        var json = _protector.UnprotectTimeLimited(StatePurpose, protectedState);
        if (json is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<EDocumentAuthorizationState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>RFC 7636: 32 rastgele bayttan 43 karakterlik doğrulayıcı; meydan okuma = BASE64URL(SHA256(doğrulayıcı)).</summary>
    public static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public string? ProviderName(string? providerCode)
        => providerCode is null ? null : _providers.Find(providerCode)?.DisplayName ?? providerCode;

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}

/// <summary>OAuth state içeriği (şifreli taşınır): kim, hangi klinik için, hangi sağlayıcıyla, PKCE doğrulayıcı.</summary>
public sealed record EDocumentAuthorizationState(Guid TenantId, Guid ClinicId, Guid UserId, string Provider, string Verifier);
