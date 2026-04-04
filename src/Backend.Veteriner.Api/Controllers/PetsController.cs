using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Application.Pets.Commands.Update;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Queries.GetById;
using Backend.Veteriner.Application.Pets.Queries.GetHistorySummary;
using Backend.Veteriner.Application.Pets.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Hayvan kayitlari. Kiraci JWT <c>tenant_id</c>; handler'da <see cref="ITenantContext"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/pets")]
[Produces("application/json")]
[Authorize]
public sealed class PetsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public PetsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Pets.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePetCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id
            },
            id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Pets.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdatePetCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (cmd.Id != Guid.Empty && id != cmd.Id)
            return Result.Failure("Pets.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        cmd = cmd with { Id = id };

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Pets.Read)]
    [ProducesResponseType(typeof(PetDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetPetByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Pet detail için klinik geçmişi özeti: randevu, muayene, tedavi, reçete, lab, yatış ve ödeme blokları tek yanıtta; her blok en yeni tarih önce, üst sınırlı.
    /// Aktif klinik bağlamı varsa kayıtlar bu kliniğe indirgenir; yoksa tenant içindeki tüm klinikler.
    /// </summary>
    [HttpGet("{id:guid}/history-summary")]
    [Authorize(Policy = PermissionCatalog.Pets.Read)]
    [ProducesResponseType(typeof(PetHistorySummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHistorySummary([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetPetHistorySummaryQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Sayfalı hayvan listesi. <c>search</c> (veya <c>page.search</c>) ad, tür adı, serbest ırk metni / katalog ırk adı ve müşteri bilgisinde arar.
    /// Opsiyonel <c>clientId</c>, <c>speciesId</c> ile AND. <c>sort</c>/<c>order</c> işlenmez.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Pets.Read)]
    [ProducesResponseType(typeof(PagedResult<PetListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? speciesId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(
            new GetPetsListQuery(PageRequestQuery.WithMergedSearch(page, search), clientId, speciesId),
            ct);
        return result.ToActionResult(this);
    }
}
