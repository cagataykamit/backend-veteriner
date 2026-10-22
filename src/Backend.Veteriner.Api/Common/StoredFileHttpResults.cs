using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Files;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Backend.Veteriner.Api.Common;

/// <summary>
/// Dosya yanıtı (IMAGING_API_CONTRACT.md §2): sabit <c>Content-Type</c>, <c>inline</c>/<c>attachment</c> ve RFC 6266 adı,
/// önbelleğe alınmaz. <c>nosniff</c>, CSP ve <c>X-Frame-Options</c> genel güvenlik başlıklarındandır.
/// </summary>
public static class StoredFileHttpResults
{
    public static IActionResult ToFileResult(this Result<StoredFileContent> result, ControllerBase controller, bool download)
    {
        if (!result.IsSuccess)
            return result.ToActionResult(controller);

        var content = result.Value!;
        var disposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline");
        disposition.SetHttpFileName(content.DownloadFileName);

        var headers = controller.Response.Headers;
        headers[HeaderNames.ContentDisposition] = disposition.ToString();
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.XContentTypeOptions] = "nosniff";

        return new FileStreamResult(content.Content, content.ContentType);
    }

    /// <summary>Yükleme uçları için tek dosya alanı; boş/eksik dosya <c>Files.Validation</c>.</summary>
    public static Result ValidateUpload(IFormFile? file)
        => file is null || file.Length <= 0
            ? Result.Failure("Files.Validation", "Yüklenecek dosya seçilmedi veya dosya boş.")
            : Result.Success();

    /// <summary>Yükleme uçlarının gövde sınırı (<see cref="FileStorageOptions.MaxRequestBytes"/>).</summary>
    public const long MaxRequestBytes = FileStorageOptions.MaxRequestBytes;
}
