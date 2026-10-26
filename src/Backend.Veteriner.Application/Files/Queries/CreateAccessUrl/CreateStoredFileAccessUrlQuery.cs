using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Files.Queries.CreateAccessUrl;

/// <summary>
/// <c>&lt;img src&gt;</c> için kısa ömürlü imzalı URL (IMAGING_API_CONTRACT.md §5). Yetki ve kapsam akış ucuyla aynıdır;
/// durum değiştirmez (okuma), denetime yazılmaz.
/// </summary>
public sealed record CreateStoredFileAccessUrlQuery(StoredFileOwnerType OwnerType, Guid OwnerId, Guid FileId)
    : IRequest<Result<StoredFileAccessUrlDto>>;

public sealed class CreateStoredFileAccessUrlQueryHandler(
    ITenantContext tenantContext,
    StoredFileContentReader reader,
    IFileAccessSigner signer,
    IOptions<FileStorageOptions> options,
    TimeProvider timeProvider)
    : IRequestHandler<CreateStoredFileAccessUrlQuery, Result<StoredFileAccessUrlDto>>
{
    /// <summary>İmzalı içerik ucunun API köküne göre yolu.</summary>
    public const string SignedContentPath = "/api/v1/files/signed";

    public async ValueTask<Result<StoredFileAccessUrlDto>> Handle(CreateStoredFileAccessUrlQuery request, CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<StoredFileAccessUrlDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var file = await reader.FindReadableAsync(tenantId, request.OwnerType, request.OwnerId, request.FileId, ct);
        if (!file.IsSuccess)
            return Result<StoredFileAccessUrlDto>.Failure(file.Error);

        var expiresAt = timeProvider.GetUtcNow().Add(options.Value.EffectiveAccessUrlLifetime);
        var expires = expiresAt.ToUnixTimeSeconds();
        var signature = signer.Sign(request.FileId, expires);
        var url = $"{SignedContentPath}/{request.FileId:D}?expires={expires}&signature={Uri.EscapeDataString(signature)}";

        return Result<StoredFileAccessUrlDto>.Success(
            new StoredFileAccessUrlDto(url, DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime));
    }
}
