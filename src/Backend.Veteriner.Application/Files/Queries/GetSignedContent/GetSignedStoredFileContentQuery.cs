using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Files.Queries.GetSignedContent;

/// <summary>
/// İmzalı URL ile anonim içerik (Bearer yok). İmza bozuk/eksik veya süresi geçmişse <c>Files.AccessUrlInvalid</c> (403);
/// dosya artık hazır değilse (ör. kayıt iptal edildi) <c>Files.NotFound</c>.
/// </summary>
public sealed record GetSignedStoredFileContentQuery(Guid FileId, long Expires, string? Signature)
    : IRequest<Result<StoredFileContent>>;

public sealed class GetSignedStoredFileContentQueryHandler(
    StoredFileContentReader reader,
    IFileAccessSigner signer,
    ITenantScope tenantScope,
    TimeProvider timeProvider)
    : IRequestHandler<GetSignedStoredFileContentQuery, Result<StoredFileContent>>
{
    public async ValueTask<Result<StoredFileContent>> Handle(GetSignedStoredFileContentQuery request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (request.Expires < now || !signer.IsValid(request.FileId, request.Expires, request.Signature))
        {
            return Result<StoredFileContent>.Failure(
                "Files.AccessUrlInvalid",
                "Dosya bağlantısı geçersiz veya süresi dolmuş.");
        }

        // İmzalı bağlantı anonimdir ve yetkiyi imza taşır: dosya kimlikle kiracılar arası bulunur (ADR-040).
        Result<StoredFile> file;
        using (tenantScope.AsSystem())
            file = await reader.FindReadyByIdAsync(request.FileId, ct);
        return file.IsSuccess
            ? await reader.OpenAsync(file.Value!, ct)
            : Result<StoredFileContent>.Failure(file.Error);
    }
}
