using Ardalis.Specification;
using Backend.Veteriner.Domain.Files;

namespace Backend.Veteriner.Application.Files.Specs;

/// <summary>Sahibin hazır dosyaları, yükleme sırasına göre (liste ve detay).</summary>
public sealed class StoredFilesReadyByOwnerSpec : Specification<StoredFile>
{
    public StoredFilesReadyByOwnerSpec(Guid tenantId, StoredFileOwnerType ownerType, Guid ownerId)
    {
        Query.AsNoTracking();
        Query.Where(f => f.TenantId == tenantId
                         && f.OwnerType == ownerType
                         && f.OwnerId == ownerId
                         && f.Status == StoredFileStatus.Ready)
            .OrderBy(f => f.CreatedAtUtc)
            .ThenBy(f => f.Id);
    }
}

/// <summary>Sahibin erişime açık veya yazılmakta olan dosyaları: sayı sınırı ve iptalde erişimi kapatma için (izlenir).</summary>
public sealed class StoredFilesOpenByOwnerSpec : Specification<StoredFile>
{
    public StoredFilesOpenByOwnerSpec(Guid tenantId, StoredFileOwnerType ownerType, Guid ownerId)
    {
        Query.Where(f => f.TenantId == tenantId
                         && f.OwnerType == ownerType
                         && f.OwnerId == ownerId
                         && f.Status != StoredFileStatus.Deleted);
    }
}

/// <summary>Sahiplerin hazır dosyalarının sahip kimlikleri (listede dosya sayısı; sayım bellekte).</summary>
public sealed class StoredFileReadyOwnerIdsSpec : Specification<StoredFile, Guid>
{
    public StoredFileReadyOwnerIdsSpec(Guid tenantId, StoredFileOwnerType ownerType, IReadOnlyCollection<Guid> ownerIds)
    {
        Query.AsNoTracking();
        Query.Where(f => f.TenantId == tenantId
                         && f.OwnerType == ownerType
                         && ownerIds.Contains(f.OwnerId)
                         && f.Status == StoredFileStatus.Ready);
        Query.Select(f => f.OwnerId);
    }
}

public sealed class StoredFileByIdSpec : Specification<StoredFile>
{
    /// <summary>Kiracı kapsamlı; <paramref name="tenantId"/> boşsa (imzalı URL) yalnız kimlikle.</summary>
    public StoredFileByIdSpec(Guid? tenantId, Guid id)
    {
        Query.AsNoTracking();
        if (tenantId is { } t)
            Query.Where(f => f.TenantId == t && f.Id == id);
        else
            Query.Where(f => f.Id == id);
    }
}

/// <summary>Yarım kalmış yüklemeler (R4 bakım işi), en eskiden.</summary>
public sealed class StoredFilesStaleUploadSpec : Specification<StoredFile>
{
    public StoredFilesStaleUploadSpec(DateTime nowUtc, TimeSpan staleAfter, int take)
    {
        Query.Where(StoredFile.IsStaleUpload(nowUtc, staleAfter))
            .OrderBy(f => f.CreatedAtUtc)
            .Take(take);
    }
}

/// <summary>Temizlik anında hâlâ yarım mı (aradaki sürede tamamlanan yükleme atlanır); güncellemek için izlenmez, sürümle eklenir.</summary>
public sealed class StoredFilesStaleUploadByIdSpec : Specification<StoredFile>
{
    public StoredFilesStaleUploadByIdSpec(Guid id, DateTime nowUtc, TimeSpan staleAfter)
    {
        Query.Where(f => f.Id == id).Where(StoredFile.IsStaleUpload(nowUtc, staleAfter));
    }
}
