using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>Belge kaydına düşmeyen (istek anında dönen) sağlayıcı hatalarının API hata koduna çevrilmesi.</summary>
public static class EDocumentErrors
{
    public static Error FromProvider(ProviderError error)
        => error.Kind switch
        {
            EDocumentErrorKind.Transient => new Error("EDocuments.ProviderUnavailable", error.Message),
            EDocumentErrorKind.Authorization => new Error("EDocuments.NotConnected", error.Message),
            _ => new Error("EDocuments.ProviderRejected", error.Message,
                error.ProviderCode is null ? null : new Dictionary<string, object?> { ["providerCode"] = error.ProviderCode }),
        };

    public static Error NotFound() => new("EDocuments.NotFound", "Belge bulunamadı.");

    public static Error ConcurrencyConflict()
        => new("EDocuments.ConcurrencyConflict", "Belge aynı anda başka bir işlemle güncellendi; sayfayı yenileyin.");
}
