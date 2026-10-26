using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.ClinicalTerms.Commands.Create;
using Backend.Veteriner.Application.ClinicalTerms.Commands.SetActive;
using Backend.Veteriner.Application.ClinicalTerms.Commands.Update;
using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.ClinicalTerms.Queries.GetById;
using Backend.Veteriner.Application.ClinicalTerms.Queries.GetList;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Diagnosis/complaint dictionary (system defaults + clinic-specific); see docs/DIAGNOSIS_DICTIONARY_API_CONTRACT.md.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinical-terms")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicalTermsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicalTermsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Paged search; <c>search</c> is Turkish-insensitive, every word must match. <c>sort</c>/<c>order</c> are not applied.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.ClinicalTerms.Read)]
    [ProducesResponseType(typeof(PagedResult<ClinicalTermDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] ClinicalTermKind? kind = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(new GetClinicalTermsListQuery(paging, kind, includeInactive), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.ClinicalTerms.Read)]
    [ProducesResponseType(typeof(ClinicalTermDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClinicalTermByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.ClinicalTerms.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicalTermDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateClinicalTermCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = result.Value!.Id },
            result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.ClinicalTerms.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicalTermDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateClinicalTermBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("ClinicalTerms.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        var result = await _mediator.Send(new UpdateClinicalTermCommand(id, body.Name, body.RowVersion), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.ClinicalTerms.Deactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SetClinicalTermActiveCommand(id, IsActive: false), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.ClinicalTerms.Deactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SetClinicalTermActiveCommand(id, IsActive: true), ct);
        return result.ToActionResult(this);
    }
}

/// <summary>PUT /api/v1/clinical-terms/{id} body; route id is source of truth.</summary>
public sealed class UpdateClinicalTermBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public string Name { get; init; } = default!;

    /// <summary>Base64 version from GET/list; required.</summary>
    public string? RowVersion { get; init; }
}
