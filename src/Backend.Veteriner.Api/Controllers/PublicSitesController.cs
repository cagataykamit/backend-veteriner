using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.ClinicSites;
using Backend.Veteriner.Application.ClinicSites.Public;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Herkese açık klinik sitesi (kimliksiz; ADR-045 K8/K10/K14; docs/CLINIC_SITE_API_CONTRACT.md §6). Yalnız yayınlanmış sürüm ve
/// klinik kaydının izinli alanları döner. Eski adres ve kapalı site 200 ile durum taşır; olmayan site 404. IP başına hız sınırlı.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public/sites/{slug}")]
[Produces("application/json")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.PublicSitePolicy)]
public sealed class PublicSitesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicSitesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Yayınlanmış site, eski adres için yönlendirme ya da kapalı durumu.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PublicSiteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] string slug, CancellationToken ct)
        => (await _mediator.Send(new GetPublicSiteQuery(slug), ct)).ToActionResult(this);

    /// <summary>Yayındaki sürümün başvurduğu görsel; taslak görseli ve kapalı sitenin görseli 404.</summary>
    [HttpGet("images/{imageId:guid}")]
    [Produces("application/octet-stream", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImage([FromRoute] string slug, [FromRoute] Guid imageId, CancellationToken ct)
        => (await _mediator.Send(new GetPublicSiteImageQuery(slug, imageId), ct)).ToPublicImage(this);
}
