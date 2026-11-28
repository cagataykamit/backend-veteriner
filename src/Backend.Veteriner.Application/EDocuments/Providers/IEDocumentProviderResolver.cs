using Backend.Veteriner.Domain.EDocuments;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.EDocuments.Providers;

/// <summary>
/// Bağlantıdaki sağlayıcı koduna (ve ortama) göre uygulamayı bulur; bilinmeyen kod <c>null</c>. Bir entegratörün birden çok
/// ortam örneği olabilir (canlı API'de test kiracıları için test örneği, <see cref="EDocumentEnvironmentOptions"/>); ilk
/// kaydedilen örnek varsayılandır.
/// </summary>
public interface IEDocumentProviderResolver
{
    /// <summary>Entegratör başına varsayılan örnek.</summary>
    IReadOnlyList<IEDocumentProvider> All { get; }

    /// <summary>Varsayılan örnek (ad, iptal desteği gibi ortamdan bağımsız bilgiler için).</summary>
    IEDocumentProvider? Find(string providerCode);

    /// <summary>Bağlantının ortamındaki örnek; o ortamda örnek yoksa <c>null</c>.</summary>
    IEDocumentProvider? Find(string providerCode, EDocumentEnvironment environment);

    /// <summary>Kiracının bağlanacağı örnek: test kiracısına yalnız test örneği (yoksa <c>null</c>), diğerlerine varsayılan.</summary>
    IEDocumentProvider? FindForTenant(string providerCode, Guid tenantId);

    IEDocumentOAuthProvider? FindOAuth(string providerCode);
}

public sealed class EDocumentProviderResolver : IEDocumentProviderResolver
{
    private readonly IReadOnlyList<IEDocumentProvider> _providers;
    private readonly IReadOnlyList<IEDocumentProvider> _defaults;
    private readonly IReadOnlyList<IEDocumentOAuthProvider> _oauthProviders;
    private readonly EDocumentEnvironmentOptions _environment;

    public EDocumentProviderResolver(
        IEnumerable<IEDocumentProvider> providers,
        IEnumerable<IEDocumentOAuthProvider> oauthProviders,
        IOptions<EDocumentEnvironmentOptions> environment)
    {
        _providers = providers.ToList();
        _defaults = _providers.DistinctBy(p => p.ProviderCode, StringComparer.OrdinalIgnoreCase).ToList();
        _oauthProviders = oauthProviders.ToList();
        _environment = environment.Value;
    }

    public IReadOnlyList<IEDocumentProvider> All => _defaults;

    public IEDocumentProvider? Find(string providerCode)
        => _defaults.FirstOrDefault(p => Matches(p, providerCode));

    public IEDocumentProvider? Find(string providerCode, EDocumentEnvironment environment)
        => _providers.FirstOrDefault(p => Matches(p, providerCode) && p.Environment == environment);

    public IEDocumentProvider? FindForTenant(string providerCode, Guid tenantId)
        => _environment.IsTestTenant(tenantId) ? Find(providerCode, EDocumentEnvironment.Test) : Find(providerCode);

    public IEDocumentOAuthProvider? FindOAuth(string providerCode)
        => _oauthProviders.FirstOrDefault(p => string.Equals(p.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));

    private static bool Matches(IEDocumentProvider provider, string providerCode)
        => string.Equals(provider.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase);
}
