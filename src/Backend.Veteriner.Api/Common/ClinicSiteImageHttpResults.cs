using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.ClinicSites;
using Backend.Veteriner.Domain.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Backend.Veteriner.Api.Common;

/// <summary>
/// Site görseli yanıtı (docs/CLINIC_SITE_API_CONTRACT.md §5–6): sabit <c>Content-Type</c>, <c>inline</c>, özetten <c>ETag</c>.
/// Herkese açık yanıt kısa süre önbelleğe alınabilir; panel önizlemesi alınmaz. <c>nosniff</c> genel güvenlik başlıklarındandır.
/// </summary>
public static class ClinicSiteImageHttpResults
{
    private const string PublicCacheControl = "public, max-age=300";
    private const string PrivateCacheControl = "private, no-store";

    public static IActionResult ToPublicImage(this Result<ClinicSiteImageContent> result, ControllerBase controller)
        => ToImage(result, controller, PublicCacheControl);

    public static IActionResult ToPrivateImage(this Result<ClinicSiteImageContent> result, ControllerBase controller)
        => ToImage(result, controller, PrivateCacheControl);

    private static IActionResult ToImage(Result<ClinicSiteImageContent> result, ControllerBase controller, string cacheControl)
    {
        if (!result.IsSuccess)
            return result.ToActionResult(controller);

        var image = result.Value!;
        var headers = controller.Response.Headers;
        headers[HeaderNames.ContentDisposition] = "inline";
        headers[HeaderNames.CacheControl] = cacheControl;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";

        return new FileStreamResult(image.Content, image.ContentType)
        {
            EntityTag = new EntityTagHeaderValue($"\"{image.Sha256}\""),
        };
    }
}
