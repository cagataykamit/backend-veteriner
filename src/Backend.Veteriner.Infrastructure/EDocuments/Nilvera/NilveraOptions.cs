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

    public int TimeoutSeconds { get; init; } = 30;

    public bool IsConfigured => Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out _);
}
