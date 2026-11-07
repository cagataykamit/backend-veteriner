using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.ClinicSites;

/// <summary>
/// Site görseli yükler (ADR-045 K12; docs/CLINIC_SITE_API_CONTRACT.md §5). Site önce taslak kaydıyla oluşmuş olmalıdır. Tür
/// istemcinin bildirdiğinden değil içerikten belirlenir; PDF ve diğer türler reddedilir. Boyutlandırma bu aşamada yoktur.
/// </summary>
public sealed record UploadClinicSiteImageCommand(Guid ClinicId, long Length, Stream Content)
    : IRequest<Result<ClinicSiteImageDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicSite.Image.Upload";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>Görseli siler; taslakta ya da yayındaki sürümde kullanılıyorsa 409. Eski sürümlerdeki başvuru silmeyi engellemez.</summary>
public sealed record DeleteClinicSiteImageCommand(Guid ClinicId, Guid ImageId)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "ClinicSite.Image.Delete";
    public string? AuditTarget => $"ClinicId={ClinicId};ImageId={ImageId}";
}

/// <summary>Panel önizlemesi: yayınlanmamış görseli de açar (yetkili).</summary>
public sealed record GetClinicSiteImageContentQuery(Guid ClinicId, Guid ImageId) : IRequest<Result<ClinicSiteImageContent>>;

public sealed class UploadClinicSiteImageCommandValidator : AbstractValidator<UploadClinicSiteImageCommand>
{
    public UploadClinicSiteImageCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class DeleteClinicSiteImageCommandValidator : AbstractValidator<DeleteClinicSiteImageCommand>
{
    public DeleteClinicSiteImageCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ImageId).NotEmpty();
    }
}

public sealed class GetClinicSiteImageContentQueryValidator : AbstractValidator<GetClinicSiteImageContentQuery>
{
    public GetClinicSiteImageContentQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.ImageId).NotEmpty();
    }
}

public sealed class UploadClinicSiteImageCommandHandler : IRequestHandler<UploadClinicSiteImageCommand, Result<ClinicSiteImageDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly IRepository<ClinicSiteImage> _images;
    private readonly IFileStorage _storage;
    private readonly IFileMalwareScanner _scanner;
    private readonly ITrialQuotaGuard _trialQuota;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UploadClinicSiteImageCommandHandler> _logger;

    public UploadClinicSiteImageCommandHandler(
        ClinicDefinitionAccess access,
        IClientContext clientContext,
        IReadRepository<ClinicSite> sites,
        IRepository<ClinicSiteImage> images,
        IFileStorage storage,
        IFileMalwareScanner scanner,
        ITrialQuotaGuard trialQuota,
        TimeProvider timeProvider,
        ILogger<UploadClinicSiteImageCommandHandler> logger)
    {
        _access = access;
        _clientContext = clientContext;
        _sites = sites;
        _images = images;
        _storage = storage;
        _scanner = scanner;
        _trialQuota = trialQuota;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<Result<ClinicSiteImageDto>> Handle(UploadClinicSiteImageCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteImageDto>.Failure(access.Error);
        var tenantId = access.Value;

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        if (site is null)
            return Result<ClinicSiteImageDto>.Failure(ClinicSiteErrors.NotFound);

        if (request.Length <= 0)
            return Fail("Files.Validation", "Dosya boş olamaz.");
        if (request.Length > ClinicSiteImage.MaxSizeBytes)
            return TooLarge();

        var quota = await _trialQuota.EnsureCanStoreAsync(tenantId, request.Length, ct);
        if (!quota.IsSuccess)
            return Result<ClinicSiteImageDto>.Failure(quota.Error);

        var count = await _images.CountAsync(new ClinicSiteImagesBySiteSpec(tenantId, site.Id), ct);
        if (count >= ClinicSiteImage.MaxImagesPerSite)
            return Fail("Files.LimitReached", $"Bir sitede en çok {ClinicSiteImage.MaxImagesPerSite} görsel olabilir.");

        var type = await DetectImageTypeAsync(request.Content, ct);
        if (type is null)
            return Fail("Files.UnsupportedType", "Yalnız JPEG, PNG ve WebP görsel yüklenebilir.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var imageId = Guid.NewGuid();
        var storageKey = StoredFile.BuildStorageKey(tenantId, request.ClinicId, utcNow, imageId);

        await using var hashing = new HashingLimitedReadStream(request.Content, ClinicSiteImage.MaxSizeBytes);
        try
        {
            await _storage.WriteAsync(storageKey, hashing, ct);
        }
        catch (FileTooLargeException)
        {
            await DiscardAsync(storageKey);
            return TooLarge();
        }
        catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Site image write failed. ImageId={ImageId} Exception={ExceptionType}", imageId, ex.GetType().FullName);
            return Fail("Files.StorageUnavailable", "Dosya deposu şu anda kullanılamıyor; daha sonra tekrar deneyin.");
        }

        var scan = await _scanner.ScanAsync(storageKey, ct);
        if (!scan.IsSuccess)
        {
            await DiscardAsync(storageKey);
            return Result<ClinicSiteImageDto>.Failure(scan.Error);
        }

        var image = ClinicSiteImage.Create(
            imageId, tenantId, request.ClinicId, site.Id, storageKey, type, hashing.BytesRead, hashing.HashHex, _clientContext.UserId!.Value, utcNow);
        await _images.AddAsync(image, ct);
        try
        {
            await _images.SaveChangesAsync(ct);
        }
        catch
        {
            await DiscardAsync(storageKey);
            throw;
        }

        return Result<ClinicSiteImageDto>.Success(ClinicSiteMapping.ToImageDto(image));
    }

    /// <summary>İlk baytlardan görsel türü; izinli tür değilse <c>null</c>. Akış başa sarılır (yazma tüm içeriği okur).</summary>
    private static async Task<StoredFileType?> DetectImageTypeAsync(Stream content, CancellationToken ct)
    {
        if (!content.CanSeek)
            throw new InvalidOperationException("Yükleme akışı konumlanabilir olmalıdır.");

        var header = new byte[StoredFileType.SignatureLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        content.Seek(0, SeekOrigin.Begin);
        return StoredFileType.Detect(header.AsSpan(0, read)) is { IsImage: true } type ? type : null;
    }

    private async Task DiscardAsync(string storageKey)
    {
        try
        {
            await _storage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Site image cleanup failed. Exception={ExceptionType}", ex.GetType().FullName);
        }
    }

    private static Result<ClinicSiteImageDto> TooLarge()
        => Fail("Files.TooLarge", $"Görsel en çok {ClinicSiteImage.MaxSizeBytes / (1024 * 1024)} MB olabilir.");

    private static Result<ClinicSiteImageDto> Fail(string code, string message)
        => Result<ClinicSiteImageDto>.Failure(code, message);
}

public sealed class DeleteClinicSiteImageCommandHandler : IRequestHandler<DeleteClinicSiteImageCommand, Result>
{
    private const string UsedInDraft = "draft";
    private const string UsedInPublished = "published";

    private readonly ClinicDefinitionAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly IReadRepository<ClinicSiteVersion> _versions;
    private readonly IRepository<ClinicSiteImage> _images;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeleteClinicSiteImageCommandHandler> _logger;

    public DeleteClinicSiteImageCommandHandler(
        ClinicDefinitionAccess access,
        IClientContext clientContext,
        IReadRepository<ClinicSite> sites,
        IReadRepository<ClinicSiteVersion> versions,
        IRepository<ClinicSiteImage> images,
        IFileStorage storage,
        TimeProvider timeProvider,
        ILogger<DeleteClinicSiteImageCommandHandler> logger)
    {
        _access = access;
        _clientContext = clientContext;
        _sites = sites;
        _versions = versions;
        _images = images;
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<Result> Handle(DeleteClinicSiteImageCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error);
        var tenantId = access.Value;

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        if (site is null)
            return Result.Failure(ClinicSiteErrors.NotFound);

        var image = await _images.FirstOrDefaultAsync(new ClinicSiteImageByIdSpec(tenantId, site.Id, request.ImageId), ct);
        if (image is null)
            return Result.Failure(ClinicSiteErrors.ImageNotFound);

        var usedIn = await FindUsageAsync(tenantId, site, request.ImageId, ct);
        if (usedIn.Count > 0)
            return Result.Failure(ClinicSiteErrors.ImageInUse(usedIn));

        image.MarkDeleted(_clientContext.UserId!.Value, _timeProvider.GetUtcNow().UtcDateTime);
        await _images.SaveChangesAsync(ct);

        // Kayıt kapandı, görsel artık sunulmaz; içerik silinemezse yalnız disk artığı kalır.
        try
        {
            await _storage.DeleteAsync(image.StorageKey, CancellationToken.None);
        }
        catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Site image content delete failed. ImageId={ImageId} Exception={ExceptionType}", image.Id, ex.GetType().FullName);
        }

        return Result.Success();
    }

    private async Task<IReadOnlyList<string>> FindUsageAsync(Guid tenantId, ClinicSite site, Guid imageId, CancellationToken ct)
    {
        var usedIn = new List<string>(2);
        var draft = ClinicSiteConfig.Parse(site.DraftConfigJson);
        if (draft.IsSuccess && draft.Value!.ImageIds.Contains(imageId))
            usedIn.Add(UsedInDraft);

        if (site.PublishedVersionNumber is { } number)
        {
            var published = await _versions.FirstOrDefaultAsync(new ClinicSiteVersionByNumberSpec(tenantId, site.Id, number), ct);
            if (published is not null && published.ReferencesImage(imageId))
                usedIn.Add(UsedInPublished);
        }

        return usedIn;
    }
}

public sealed class GetClinicSiteImageContentQueryHandler : IRequestHandler<GetClinicSiteImageContentQuery, Result<ClinicSiteImageContent>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly IReadRepository<ClinicSiteImage> _images;
    private readonly ClinicSiteImageContentReader _reader;

    public GetClinicSiteImageContentQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<ClinicSite> sites,
        IReadRepository<ClinicSiteImage> images,
        ClinicSiteImageContentReader reader)
    {
        _access = access;
        _sites = sites;
        _images = images;
        _reader = reader;
    }

    public async ValueTask<Result<ClinicSiteImageContent>> Handle(GetClinicSiteImageContentQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteImageContent>.Failure(access.Error);
        var tenantId = access.Value;

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        if (site is null)
            return Result<ClinicSiteImageContent>.Failure(ClinicSiteErrors.NotFound);

        var image = await _images.FirstOrDefaultAsync(new ClinicSiteImageByIdSpec(tenantId, site.Id, request.ImageId), ct);
        return image is null
            ? Result<ClinicSiteImageContent>.Failure(ClinicSiteErrors.ImageNotFound)
            : await _reader.OpenAsync(image, ct);
    }
}

/// <summary>Görsel içeriğini depodan açar; depo hatası 503, eksik içerik 404.</summary>
public sealed class ClinicSiteImageContentReader
{
    private readonly IFileStorage _storage;
    private readonly ILogger<ClinicSiteImageContentReader> _logger;

    public ClinicSiteImageContentReader(IFileStorage storage, ILogger<ClinicSiteImageContentReader> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result<ClinicSiteImageContent>> OpenAsync(ClinicSiteImage image, CancellationToken ct)
    {
        Stream? stream;
        try
        {
            stream = await _storage.OpenReadAsync(image.StorageKey, ct);
        }
        catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Site image read failed. ImageId={ImageId} Exception={ExceptionType}", image.Id, ex.GetType().FullName);
            return Result<ClinicSiteImageContent>.Failure(
                "Files.StorageUnavailable",
                "Dosya deposu şu anda kullanılamıyor; daha sonra tekrar deneyin.");
        }

        if (stream is null)
        {
            _logger.LogError("Site image content missing in storage. ImageId={ImageId}", image.Id);
            return Result<ClinicSiteImageContent>.Failure(ClinicSiteErrors.ImageNotFound);
        }

        return Result<ClinicSiteImageContent>.Success(new ClinicSiteImageContent(stream, image.ContentType, image.Sha256, image.SizeBytes));
    }
}
