using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Domain.Files;

namespace Backend.Veteriner.Application.Files;

/// <summary>
/// Sahip kayıt iptal edilince dosyalarının erişimini kapatır (yumuşak silme; fiziksel içerik kalır, ASAMA6 D10).
/// Değişiklikler izlenen varlıklara yapılır, kalıcı yazma sahibin güncellemesiyle aynı <c>SaveChanges</c>'tedir.
/// </summary>
public sealed class StoredFileAccessCloser(IRepository<StoredFile> files)
{
    public async Task MarkOwnerFilesDeletedAsync(
        Guid tenantId,
        StoredFileOwnerType ownerType,
        Guid ownerId,
        Guid deletedByUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var open = await files.ListAsync(new StoredFilesOpenByOwnerSpec(tenantId, ownerType, ownerId), ct);
        foreach (var file in open)
            file.MarkDeleted(deletedByUserId, nowUtc);
    }
}
