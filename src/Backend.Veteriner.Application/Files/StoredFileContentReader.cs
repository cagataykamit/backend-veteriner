using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Files;

/// <summary>
/// Dosya erişiminin tek kuralı (ASAMA6 R3): dosya ancak sahibi okunabiliyorsa, URL'deki sahibe aitse ve <c>Ready</c> ise açılır;
/// aksi hâlde <c>Files.NotFound</c>. Akış ucu, imzalı URL üretimi ve imzalı içerik aynı yöntemleri kullanır.
/// </summary>
public sealed class StoredFileContentReader
{
    public const string NotFoundCode = "Files.NotFound";

    private readonly IEnumerable<IStoredFileOwnerAccess> _ownerAccesses;
    private readonly IReadRepository<StoredFile> _files;
    private readonly IFileStorage _storage;
    private readonly ILogger<StoredFileContentReader> _logger;

    public StoredFileContentReader(
        IEnumerable<IStoredFileOwnerAccess> ownerAccesses,
        IReadRepository<StoredFile> files,
        IFileStorage storage,
        ILogger<StoredFileContentReader> logger)
    {
        _ownerAccesses = ownerAccesses;
        _files = files;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Sahip kaydın okuma kapsamında ve ona ait hazır dosya.</summary>
    public async Task<Result<StoredFile>> FindReadableAsync(
        Guid tenantId,
        StoredFileOwnerType ownerType,
        Guid ownerId,
        Guid fileId,
        CancellationToken ct)
    {
        var owner = await _ownerAccesses.For(ownerType).ResolveForReadAsync(tenantId, ownerId, ct);
        if (!owner.IsSuccess)
            return Result<StoredFile>.Failure(owner.Error);

        var file = await _files.FirstOrDefaultAsync(new StoredFileByIdSpec(tenantId, fileId), ct);
        return file is not null
               && file.OwnerType == ownerType
               && file.OwnerId == ownerId
               && file.Status == StoredFileStatus.Ready
            ? Result<StoredFile>.Success(file)
            : NotFound<StoredFile>();
    }

    /// <summary>İmzası doğrulanmış erişim: kiracı bağlamı yoktur, yalnız dosyanın hazır olması gerekir.</summary>
    public async Task<Result<StoredFile>> FindReadyByIdAsync(Guid fileId, CancellationToken ct)
    {
        var file = await _files.FirstOrDefaultAsync(new StoredFileByIdSpec(null, fileId), ct);
        return file is { Status: StoredFileStatus.Ready }
            ? Result<StoredFile>.Success(file)
            : NotFound<StoredFile>();
    }

    public async Task<Result<StoredFileContent>> OpenAsync(StoredFile file, CancellationToken ct)
    {
        Stream? stream;
        try
        {
            stream = await _storage.OpenReadAsync(file.StorageKey, ct);
        }
        catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError("File read failed. FileId={FileId} Exception={ExceptionType}", file.Id, ex.GetType().FullName);
            return Result<StoredFileContent>.Failure(
                "Files.StorageUnavailable",
                "Dosya deposu şu anda kullanılamıyor; daha sonra tekrar deneyin.");
        }

        if (stream is null)
        {
            _logger.LogError("Ready file content missing in storage. FileId={FileId}", file.Id);
            return NotFound<StoredFileContent>();
        }

        var type = file.Type;
        return Result<StoredFileContent>.Success(new StoredFileContent(
            stream,
            type.ContentType,
            StoredFileName.ForDownload(file.OriginalFileName, type),
            file.SizeBytes));
    }

    private static Result<T> NotFound<T>() => Result<T>.Failure(NotFoundCode, "Dosya bulunamadı.");
}
