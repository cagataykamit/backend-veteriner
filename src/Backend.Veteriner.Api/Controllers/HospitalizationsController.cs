using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Hospitalizations.Commands.Create;
using Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;
using Backend.Veteriner.Application.Hospitalizations.Commands.Update;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetById;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>In-clinic hospitalization / observation stays for pets (optional link to examination).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/hospitalizations")]
[Produces("application/json")]
[Authorize]
public sealed class HospitalizationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public HospitalizationsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Hospitalizations.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateHospitalizationCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Hospitalizations.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateHospitalizationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Hospitalizations.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var cmd = new UpdateHospitalizationCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.ExaminationId,
            body.AdmittedAtUtc,
            body.PlannedDischargeAtUtc,
            body.Reason,
            body.Notes);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/discharge")]
    [Authorize(Policy = PermissionCatalog.Hospitalizations.Discharge)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Discharge([FromRoute] Guid id, [FromBody] DischargeHospitalizationBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var cmd = new DischargeHospitalizationCommand(id, body.DischargedAtUtc, body.Notes);
        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Hospitalizations.Read)]
    [ProducesResponseType(typeof(HospitalizationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetHospitalizationByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Paged list. <c>search</c> (or <c>page.search</c>): reason, notes, and pet/client name match.
    /// <c>activeOnly=true</c> limits to open stays; <c>false</c> to discharged only; omit for all.
    /// <c>sort</c>/<c>order</c> on <see cref="PageRequest"/> are not applied.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Hospitalizations.Read)]
    [ProducesResponseType(typeof(PagedResult<HospitalizationListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] bool? activeOnly = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetHospitalizationsListQuery(
                PageRequestQuery.WithMergedSearch(page, search),
                clinicId,
                petId,
                activeOnly,
                dateFromUtc,
                dateToUtc),
            ct);
        return result.ToActionResult(this);
    }
}
