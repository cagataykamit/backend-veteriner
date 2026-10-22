namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Dosya içeriği deposu (IMAGING_API_CONTRACT.md §2). Anahtarı yalnız sunucu üretir (<c>StoredFile.BuildStorageKey</c>);
/// uygulama anahtarı kök altında çözmeli, kök dışını reddetmeli ve var olan içeriği ezmemelidir. v1 uygulaması yerel disktir;
/// buluta geçiş yalnız yeni bir uygulama ister. Depo yapılandırılmamış veya erişilemiyorsa
/// <see cref="FileStorageUnavailableException"/> fırlatılır.
/// </summary>
public interface IFileStorage
{
    /// <summary>Yeni içerik yazar; aynı anahtarda içerik varsa hata verir (ezme yok).</summary>
    Task WriteAsync(string storageKey, Stream content, CancellationToken ct);

    /// <summary>İçeriği okumak için açar; yoksa <c>null</c>.</summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);

    /// <summary>İçeriği siler; yoksa etkisizdir.</summary>
    Task DeleteAsync(string storageKey, CancellationToken ct);
}

/// <summary>Depo yapılandırılmamış veya erişilemiyor (503 <c>Files.StorageUnavailable</c>).</summary>
public sealed class FileStorageUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
