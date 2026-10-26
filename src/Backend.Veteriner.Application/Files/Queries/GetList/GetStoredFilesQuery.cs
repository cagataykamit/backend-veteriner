using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Access;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Files.Queries.GetList;

/// <summary>Sahip kaydın hazır dosyaları, yükleme sırasına göre.</summary>
public sealed record GetStoredFilesQuery(StoredFileOwnerType OwnerType, Guid OwnerId)
    : IRequest<Result<IReadOnlyList<StoredFileDto>>>;

public sealed class GetStoredFilesQueryHandler(
    ITenantContext tenantContext,
    IEnumerable<IStoredFileOwnerAccess> ownerAccesses,
    IReadRepository<StoredFile> files)
    : IRequestHandler<GetStoredFilesQuery, Result<IReadOnlyList<StoredFileDto>>>
{
    public async ValueTask<Result<IReadOnlyList<StoredFileDto>>> Handle(GetStoredFilesQuery request, CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<StoredFileDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var owner = await ownerAccesses.For(request.OwnerType).ResolveForReadAsync(tenantId, request.OwnerId, ct);
        if (!owner.IsSuccess)
            return Result<IReadOnlyList<StoredFileDto>>.Failure(owner.Error);

        var rows = await files.ListAsync(new StoredFilesReadyByOwnerSpec(tenantId, request.OwnerType, request.OwnerId), ct);
        return Result<IReadOnlyList<StoredFileDto>>.Success(rows.Select(StoredFileDto.From).ToList());
    }
}
