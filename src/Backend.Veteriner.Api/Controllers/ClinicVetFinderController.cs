using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VetFinder;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Kliniğin Veteriner bul kaydı (ADR-047 K4; docs/VET_FINDER_API_CONTRACT.md §2). Ayarlar mevcut klinik ayar yetkisiyle: okuma
/// <c>Clinics.Read</c>, yazma <c>Clinics.Update</c>. Tüm paketlerde.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/vet-finder")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicVetFinderController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicVetFinderController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Kayıt; yoksa listelenme kapalı.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicVetFinderListingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetClinicVetFinderListingQuery(clinicId), ct)).ToActionResult(this);
    }

    /// <summary>Listelenmeyi açar/kapatır, hizmetleri ve isteğe bağlı konumu yazar; kapatınca klinik hemen listeden çıkar.</summary>
    [HttpPut]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicVetFinderListingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Put([FromRoute] Guid clinicId, [FromBody] VetFinderListingBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(
            new UpdateClinicVetFinderListingCommand(clinicId, body.IsListed, body.Services ?? [], body.Latitude, body.Longitude), ct))
            .ToActionResult(this);
    }
}

/// <summary>PUT /api/v1/clinics/{clinicId}/vet-finder body.</summary>
public sealed class VetFinderListingBody
{
    public bool IsListed { get; init; }
    public IReadOnlyList<string>? Services { get; init; }
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
}
