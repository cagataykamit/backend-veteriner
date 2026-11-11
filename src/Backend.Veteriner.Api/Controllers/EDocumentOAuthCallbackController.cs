using Asp.Versioning;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.EDocuments.Connections;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Entegratör OAuth dönüşü. Yol <b>sürüm içermez</b> (<c>/api/e-documents/{sağlayıcı}/callback</c>): Paraşüt'e kayıtlı yönlendirme
/// adresleri kalıcıdır ve değiştirilemez. Anonim; kiracı ve klinik şifreli <c>state</c>'ten çözülür. Sonunda tarayıcı panelin
/// e-Belge ayar sayfasına yalnız sonuç koduyla döner (belirteç veya kişisel veri taşınmaz).
/// </summary>
[ApiController]
[ApiVersionNeutral]
[Route("api/e-documents")]
[AllowAnonymous]
[EnableRateLimiting("billing-callback")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class EDocumentOAuthCallbackController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly AppOptions _appOptions;

    public EDocumentOAuthCallbackController(IMediator mediator, IOptions<AppOptions> appOptions)
    {
        _mediator = mediator;
        _appOptions = appOptions.Value;
    }

    [HttpGet("{provider}/callback")]
    public async Task<IActionResult> Callback(
        [FromRoute] string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var outcome = await _mediator.Send(new CompleteEDocumentAuthorizationCommand(provider, code, state, error), ct);
        return Redirect(PanelUrl(outcome));
    }

    private string PanelUrl(EDocumentAuthorizationOutcome outcome)
    {
        var baseUrl = _appOptions.FrontendBaseUrl.TrimEnd('/');
        var query = $"eDocumentConnection={Uri.EscapeDataString(outcome.Outcome)}";
        if (outcome.ClinicId is { } clinicId)
            query += $"&clinicId={clinicId}";
        if (!string.IsNullOrEmpty(outcome.Reason))
            query += $"&reason={Uri.EscapeDataString(outcome.Reason)}";
        return $"{baseUrl}/panel/settings/e-documents?{query}";
    }
}
