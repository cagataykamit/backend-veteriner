using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Files.Access;

/// <summary>
/// Dosyanın sahip kaydı üzerinden erişim (ASAMA6 R3): her sahip türü kendi kaydını kiracı + aktif klinik bağlamı + klinik
/// ataması ile çözer ve sahibin klinik kimliğini döner. İzin (Read/Update) uç politikasındadır.
/// </summary>
public interface IStoredFileOwnerAccess
{
    StoredFileOwnerType OwnerType { get; }

    /// <summary>Okuma: kayıt yok ya da kapsam dışıysa sahibin <c>NotFound</c> kodu (varlık sızdırılmaz).</summary>
    Task<Result<Guid>> ResolveForReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct);

    /// <summary>Dosya ekleme: okuma kuralı + kliniğe yazma ataması + sahibin dosya kabul durumu (iptal, kesinleşme kilidi).</summary>
    Task<Result<Guid>> ResolveForAttachAsync(Guid tenantId, Guid ownerId, CancellationToken ct);
}

internal static class StoredFileOwnerAccessExtensions
{
    public static IStoredFileOwnerAccess For(this IEnumerable<IStoredFileOwnerAccess> accesses, StoredFileOwnerType ownerType)
        => accesses.SingleOrDefault(a => a.OwnerType == ownerType)
           ?? throw new InvalidOperationException($"Dosya sahibi erişimi kayıtlı değil: {ownerType}.");
}
