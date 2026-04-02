using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Treatments.Commands.Create;
using Backend.Veteriner.Application.Treatments.Commands.Update;
using Backend.Veteriner.Application.Treatments.Contracts.Dtos;
using Backend.Veteriner.Application.Treatments.Queries.GetById;
using Backend.Veteriner.Application.Treatments.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Clinical treatment records for pets (optional link to an examination).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/treatments")]
[Produces("application/json")]
[Authorize]
public sealed class TreatmentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public TreatmentsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Treatments.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateTreatmentCommand cmd, CancellationToken ct)
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
    [Authorize(Policy = PermissionCatalog.Treatments.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateTreatmentBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Treatments.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var cmd = new UpdateTreatmentCommand(
            id,
            body.ClinicId,
            body.PetId,
            body.ExaminationId,
            body.TreatmentDateUtc,
            body.Title,
            body.Description,
            body.Notes,
            body.FollowUpDateUtc);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Treatments.Read)]
    [ProducesResponseType(typeof(TreatmentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetTreatmentByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Paged list. <c>search</c> (or <c>page.search</c>): title, description, notes, and pet/client name match (same pattern as examinations).
    /// <c>sort</c>/<c>order</c> on <see cref="PageRequest"/> are not applied.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Treatments.Read)]
    [ProducesResponseType(typeof(PagedResult<TreatmentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] DateTime? dateFromUtc = null,
        [FromQuery] DateTime? dateToUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetTreatmentsListQuery(
                PageRequestQuery.WithMergedSearch(page, search),
                clinicId,
                petId,
                dateFromUtc,
                dateToUtc),
            ct);
        return result.ToActionResult(this);
    }
}
