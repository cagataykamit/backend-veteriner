using Backend.Veteriner.Domain.EDocuments;

namespace Backend.Veteriner.Infrastructure.EDocuments.Nilvera;

/// <summary>
/// Nilvera ayarları (<c>EDocuments:Nilvera</c>). Uygulama kimliği yoktur: her klinik kendi Nilvera hesabında ürettiği API
/// anahtarıyla bağlanır (anahtar bağlantıda şifreli saklanır). Ortam ve taban adres birlikte seçilir; test anahtarı yalnız
/// test adresiyle (<c>https://apitest.nilvera.com</c>) çalışır.
/// </summary>
public sealed class NilveraOptions
{
    public const string SectionName = "EDocuments:Nilvera";

    public EDocumentEnvironment Environment { get; init; } = EDocumentEnvironment.Live;

    /// <summary>API tabanı; servis adı (<c>earchive</c>, <c>einvoice</c>, <c>evoucher</c>, <c>general</c>) yola eklenir.</summary>
    public string ApiBaseUrl { get; init; } = "https://api.nilvera.com";

    /// <summary>Test kiracılarının (<c>EDocuments:TestTenantIds</c>) bağlandığı test ortamı tabanı.</summary>
    public string TestApiBaseUrl { get; init; } = "https://apitest.nilvera.com";

    public int TimeoutSeconds { get; init; } = 30;

    public bool IsConfigured => Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out _);

    /// <summary>Aynı ayarların test ortamı kopyası (test örneği için).</summary>
    public NilveraOptions ForTest() => new()
    {
        Environment = EDocumentEnvironment.Test,
        ApiBaseUrl = TestApiBaseUrl,
        TestApiBaseUrl = TestApiBaseUrl,
        TimeoutSeconds = TimeoutSeconds,
    };
}
