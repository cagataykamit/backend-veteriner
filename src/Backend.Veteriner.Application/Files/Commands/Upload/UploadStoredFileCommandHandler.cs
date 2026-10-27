using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Files.Commands.Upload;

/// <summary>
/// Yükleme sırası (ASAMA6 R4): sahip ve kapsam → boyut → sayı sınırı → içerikten tür (magic bytes) → meta <c>Uploading</c>
/// kaydı → içerik yazımı (özet ve sınır akışta) → tarama arayüzü → <c>Ready</c>. Yazma veya tarama başarısızsa içerik silinir,
/// meta <c>Deleted</c> olur; süreç ortasında kalan kaydı bakım işi temizler.
/// </summary>
public sealed class UploadStoredFileCommandHandler : IRequestHandler<UploadStoredFileCommand, Result<StoredFileDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IEnumerable<IStoredFileOwnerAccess> _ownerAccesses;
    private readonly IRepository<StoredFile> _files;
    private readonly IFileStorage _storage;
    private readonly IFileMalwareScanner _scanner;
    private readonly FileStorageOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UploadStoredFileCommandHandler> _logger;

    public UploadStoredFileCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IEnumerable<IStoredFileOwnerAccess> ownerAccesses,
        IRepository<StoredFile> files,
        IFileStorage storage,
        IFileMalwareScanner scanner,
        IOptions<FileStorageOptions> options,
        TimeProvider timeProvider,
        ILogger<UploadStoredFileCommandHandler> logger)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _ownerAccesses = ownerAccesses;
        _files = files;
        _storage = storage;
        _scanner = scanner;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    public async ValueTask<Result<StoredFileDto>> Handle(UploadStoredFileCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Fail("Tenants.ContextMissing", "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        var owner = await _ownerAccesses.For(request.OwnerType).ResolveForAttachAsync(tenantId, request.OwnerId, ct);
        if (!owner.IsSuccess)
            return Result<StoredFileDto>.Failure(owner.Error);

        var maxBytes = _options.EffectiveMaxFileBytes;
        if (request.Length <= 0)
            return Fail("Files.Validation", "Dosya boş olamaz.");
        if (request.Length > maxBytes)
            return TooLarge(maxBytes);

        var openFiles = await _files.CountAsync(
            new StoredFilesOpenByOwnerSpec(tenantId, request.OwnerType, request.OwnerId), ct);
        if (openFiles >= _options.MaxFilesPerOwner)
        {
            return Fail(
                "Files.LimitReached",
                $"Bir kayda en çok {_options.MaxFilesPerOwner} dosya eklenebilir.");
        }

        var type = await DetectTypeAsync(request.Content, ct);
        if (type is null)
        {
            return Fail(
                "Files.UnsupportedType",
                "Yalnız JPEG, PNG, WebP görüntü ve PDF dosyası yüklenebilir.");
        }

        var file = new StoredFile(
            tenantId, owner.Value, request.OwnerType, request.OwnerId, request.FileName, type, userId, Now);
        await _files.AddAsync(file, ct);
        await _files.SaveChangesAsync(ct);

        await using var hashing = new HashingLimitedReadStream(request.Content, maxBytes);
        try
        {
            await _storage.WriteAsync(file.StorageKey, hashing, ct);
        }
        catch (FileTooLargeException)
        {
            await DiscardAsync(file, userId);
            return TooLarge(maxBytes);
        }
        catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError("File write failed. FileId={FileId} Exception={ExceptionType}", file.Id, ex.GetType().FullName);
            await DiscardAsync(file, userId);
            return StorageUnavailable();
        }
        catch (OperationCanceledException)
        {
            await DiscardAsync(file, userId);
            throw;
        }

        var scan = await _scanner.ScanAsync(file.StorageKey, ct);
        if (!scan.IsSuccess)
        {
            await DiscardAsync(file, userId);
            return Result<StoredFileDto>.Failure(scan.Error);
        }

        var ready = file.MarkReady(hashing.BytesRead, hashing.HashHex, Now);
        if (!ready.IsSuccess)
        {
            await DiscardAsync(file, userId);
            return Result<StoredFileDto>.Failure(ready.Error);
        }

        await _files.UpdateAsync(file, ct);
        await _files.SaveChangesAsync(ct);
        return Result<StoredFileDto>.Success(StoredFileDto.From(file));
    }

    /// <summary>İlk baytlardan tür; akış başa sarılır (yazma tüm içeriği okur).</summary>
    private static async Task<StoredFileType?> DetectTypeAsync(Stream content, CancellationToken ct)
    {
        if (!content.CanSeek)
            throw new InvalidOperationException("Yükleme akışı konumlanabilir olmalıdır.");

        var header = new byte[StoredFileType.SignatureLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        content.Seek(0, SeekOrigin.Begin);
        return StoredFileType.Detect(header.AsSpan(0, read));
    }

    /// <summary>Başarısız yüklemenin içeriğini siler ve kaydını kapatır; burada da hata olursa bakım işi temizler.</summary>
    private async Task DiscardAsync(StoredFile file, Guid userId)
    {
        try
        {
            await _storage.DeleteAsync(file.StorageKey, CancellationToken.None);
            file.MarkDeleted(userId, Now);
            await _files.UpdateAsync(file, CancellationToken.None);
            await _files.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed upload cleanup deferred to maintenance. FileId={FileId} Exception={ExceptionType}",
                file.Id,
                ex.GetType().FullName);
        }
    }

    private static Result<StoredFileDto> TooLarge(long maxBytes)
        => Fail("Files.TooLarge", $"Dosya en çok {maxBytes / (1024 * 1024)} MB olabilir.");

    private static Result<StoredFileDto> StorageUnavailable()
        => Fail("Files.StorageUnavailable", "Dosya deposu şu anda kullanılamıyor; daha sonra tekrar deneyin.");

    private static Result<StoredFileDto> Fail(string code, string message)
        => Result<StoredFileDto>.Failure(code, message);
}
