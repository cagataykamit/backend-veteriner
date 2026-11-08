using Asp.Versioning;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.VetFinder;
using Backend.Veteriner.Application.VetFinder.Public;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Veteriner bul (kimliksiz; ADR-047; docs/VET_FINDER_API_CONTRACT.md §3–5). Yalnız listelenmeyi açmış, aktif ve aboneliği açık
/// klinikler; kiracı ve klinik kimliği yerine kartın herkese açık anahtarı. IP başına hız sınırı klinik sitesiyle aynı politika.
/// Yanıtlar önbellek başlığı taşımaz: klinik listeden çıkınca gecikmesiz düşer.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public/vet-finder")]
[Produces("application/json")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingExtensions.PublicSitePolicy)]
public sealed class PublicVetFinderController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicVetFinderController(IMediator mediator) => _mediator = mediator;

    /// <summary>Listelenen kliniği olan il ve ilçeler, hizmet listesi ve dizin eşiği.</summary>
    [HttpGet("regions")]
    [ProducesResponseType(typeof(VetFinderRegionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRegions(CancellationToken ct)
        => (await _mediator.Send(new GetVetFinderRegionsQuery(), ct)).ToActionResult(this);

    /// <summary>Klinik listesi: önce "şu an açık", sonra yakınlık; il, ilçe ve hizmet adres biçimindedir (<c>istanbul</c>).</summary>
    [HttpGet("clinics")]
    [ProducesResponseType(typeof(VetFinderClinicListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClinics(
        [FromQuery] string? city,
        [FromQuery] string? district,
        [FromQuery] string? service,
        [FromQuery] bool? openNow,
        [FromQuery] int? page,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        CancellationToken ct)
        => (await _mediator.Send(new GetVetFinderClinicsQuery(city, district, service, openNow ?? false, page ?? 1, lat, lng), ct))
            .ToActionResult(this);

    /// <summary>Kartların en yakın müsait saati; <c>keys</c> virgülle, en çok bir sayfa kart.</summary>
    [HttpGet("next-slots")]
    [ProducesResponseType(typeof(VetFinderNextSlotsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetNextSlots([FromQuery] string? keys, CancellationToken ct)
        => (await _mediator.Send(
            new GetVetFinderNextSlotsQuery((keys ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), ct))
            .ToActionResult(this);
}
