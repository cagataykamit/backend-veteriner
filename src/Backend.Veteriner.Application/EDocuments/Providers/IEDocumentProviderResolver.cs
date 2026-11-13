namespace Backend.Veteriner.Application.EDocuments.Providers;

/// <summary>Bağlantıdaki sağlayıcı koduna göre uygulamayı bulur; bilinmeyen kod <c>null</c>.</summary>
public interface IEDocumentProviderResolver
{
    IReadOnlyList<IEDocumentProvider> All { get; }

    IEDocumentProvider? Find(string providerCode);

    IEDocumentOAuthProvider? FindOAuth(string providerCode);

    IEDocumentCredentialProvider? FindCredential(string providerCode);
}

public sealed class EDocumentProviderResolver : IEDocumentProviderResolver
{
    private readonly IReadOnlyList<IEDocumentProvider> _providers;
    private readonly IReadOnlyList<IEDocumentOAuthProvider> _oauthProviders;
    private readonly IReadOnlyList<IEDocumentCredentialProvider> _credentialProviders;

    public EDocumentProviderResolver(
        IEnumerable<IEDocumentProvider> providers,
        IEnumerable<IEDocumentOAuthProvider> oauthProviders,
        IEnumerable<IEDocumentCredentialProvider> credentialProviders)
    {
        _providers = providers.ToList();
        _oauthProviders = oauthProviders.ToList();
        _credentialProviders = credentialProviders.ToList();
    }

    public IReadOnlyList<IEDocumentProvider> All => _providers;

    public IEDocumentProvider? Find(string providerCode)
        => _providers.FirstOrDefault(p => string.Equals(p.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));

    public IEDocumentOAuthProvider? FindOAuth(string providerCode)
        => _oauthProviders.FirstOrDefault(p => string.Equals(p.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));

    public IEDocumentCredentialProvider? FindCredential(string providerCode)
        => _credentialProviders.FirstOrDefault(p => string.Equals(p.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase));
}
