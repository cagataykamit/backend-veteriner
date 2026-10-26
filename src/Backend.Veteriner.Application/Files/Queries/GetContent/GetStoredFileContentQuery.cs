using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Files.Queries.GetContent;

/// <summary>Bearer ile akış ucu (<c>GET .../files/{fileId}/content</c>). Erişim denetime yazılmaz.</summary>
public sealed record GetStoredFileContentQuery(StoredFileOwnerType OwnerType, Guid OwnerId, Guid FileId)
    : IRequest<Result<StoredFileContent>>;

public sealed class GetStoredFileContentQueryHandler(ITenantContext tenantContext, StoredFileContentReader reader)
    : IRequestHandler<GetStoredFileContentQuery, Result<StoredFileContent>>
{
    public async ValueTask<Result<StoredFileContent>> Handle(GetStoredFileContentQuery request, CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<StoredFileContent>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var file = await reader.FindReadableAsync(tenantId, request.OwnerType, request.OwnerId, request.FileId, ct);
        return file.IsSuccess
            ? await reader.OpenAsync(file.Value!, ct)
            : Result<StoredFileContent>.Failure(file.Error);
    }
}
