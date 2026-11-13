using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>
/// Bağlantının şifreli kimlik bilgisini çözüp sağlayıcıyı açar ve çağrı sonucunu bağlantıya geri yazar (yenilenen belirteç
/// yeniden şifrelenir; yetki reddi bağlantıyı "yeniden bağlan" durumuna alır). Kimlik bilgisi bu sınıf dışına düz metin çıkmaz
/// (yalnız sağlayıcı çağrısına). Değişen bağlantıyı kaydetmek çağıranın işidir.
/// </summary>
public sealed class EDocumentProviderGateway
{
    public const string CredentialsPurpose = "EDocuments.ProviderCredentials.v1";

    private readonly IEDocumentProviderResolver _providers;
    private readonly ISecretProtector _protector;
    private readonly TimeProvider _timeProvider;

    public EDocumentProviderGateway(IEDocumentProviderResolver providers, ISecretProtector protector, TimeProvider timeProvider)
    {
        _providers = providers;
        _protector = protector;
        _timeProvider = timeProvider;
    }

    public Result<EDocumentProviderSession> Open(EDocumentConnection? connection)
    {
        if (connection is null || !connection.IsUsable)
            return Result<EDocumentProviderSession>.Failure(NotConnected(connection));

        var provider = _providers.Find(connection.ProviderCode);
        if (provider is null || !provider.IsConfigured)
            return Result<EDocumentProviderSession>.Failure(ProviderNotConfigured());

        var credentials = _protector.Unprotect(CredentialsPurpose, connection.ProtectedCredentials!);
        if (credentials is null)
        {
            // Anahtar halkası değişti / veri bozuk: belirteç kullanılamaz, klinik yeniden bağlanmalı.
            connection.RequireReauthorization("Bağlantı bilgisi çözülemedi; hesabı yeniden bağlayın.", Now());
            return Result<EDocumentProviderSession>.Failure(NotConnected(connection));
        }

        return Result<EDocumentProviderSession>.Success(
            new EDocumentProviderSession(provider, new EDocumentProviderAccount(connection.AccountId!, credentials)));
    }

    /// <summary>
    /// Kesilmiş / gönderilmiş belge için sağlayıcıyı açar: klinik o arada başka sağlayıcıya veya ortama (test ↔ canlı) geçtiyse
    /// belge yeni sağlayıcıya hiç gönderilmez, sorulmaz, iptal edilmez (çapraz gönderim çift belge üretir).
    /// </summary>
    public Result<EDocumentProviderSession> OpenFor(EDocument document, EDocumentConnection? connection)
    {
        if (connection is not null
            && (!string.Equals(connection.ProviderCode, document.ProviderCode, StringComparison.Ordinal)
                || connection.Environment != document.Environment))
        {
            return Result<EDocumentProviderSession>.Failure(ProviderMismatch(document));
        }

        return Open(connection);
    }

    public Error ProviderMismatch(EDocument document)
    {
        var name = _providers.Find(document.ProviderCode)?.DisplayName ?? document.ProviderCode;
        var environment = document.Environment == EDocumentEnvironment.Test ? " test ortamı" : string.Empty;
        return new Error(
            ProviderMismatchCode,
            $"Belge {name}{environment} hesabıyla gönderildi; klinik artık başka bir entegratöre bağlı. Belgeyi {name} ekranından " +
            "takip edin; kesilmediyse vazgeçip yeniden kesin.");
    }

    public const string ProviderMismatchCode = "EDocuments.ProviderMismatch";

    /// <summary>Sağlayıcı sonucunu bağlantıya yansıtır: yenilenen belirteci saklar, yetki reddinde bağlantıyı düşürür.</summary>
    public void Absorb<T>(EDocumentConnection connection, ProviderResult<T> result)
    {
        if (result.RefreshedCredentials is { } refreshed)
            connection.ReplaceCredentials(_protector.Protect(CredentialsPurpose, refreshed), Now());

        if (result.Error is { Kind: EDocumentErrorKind.Authorization } error)
            connection.RequireReauthorization(error.Message, Now());
    }

    public string ProtectCredentials(string credentials) => _protector.Protect(CredentialsPurpose, credentials);

    public string? UnprotectCredentials(string protectedCredentials) => _protector.Unprotect(CredentialsPurpose, protectedCredentials);

    public static Error NotConnected(EDocumentConnection? connection)
        => connection?.Status == EDocumentConnectionStatus.ReauthorizationRequired
            ? new Error("EDocuments.NotConnected", "Entegratör hesabının yetkisi sona erdi; Ayarlar'dan hesabı yeniden bağlayın.")
            : new Error("EDocuments.NotConnected", "Klinik için e-belge entegratörü bağlı değil; Ayarlar'dan hesabı bağlayın.");

    public static Error ProviderNotConfigured()
        => new("EDocuments.ProviderNotConfigured", "e-belge sağlayıcısı sunucuda yapılandırılmamış.");

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}

public sealed record EDocumentProviderSession(IEDocumentProvider Provider, EDocumentProviderAccount Account);
