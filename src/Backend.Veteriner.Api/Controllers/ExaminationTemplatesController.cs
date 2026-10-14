using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.Create;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.SetActive;
using Backend.Veteriner.Application.ExaminationTemplates.Commands.Update;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.ExaminationTemplates.Queries.GetById;
using Backend.Veteriner.Application.ExaminationTemplates.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Examination templates / treatment packages (system defaults + clinic-specific); see docs/EXAMINATION_TEMPLATE_API_CONTRACT.md.
/// Applying a template to an examination is <c>POST /api/v1/examinations/{id}/apply-template</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/examination-templates")]
[Produces("application/json")]
[Authorize]
public sealed class ExaminationTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ExaminationTemplatesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Paged list with lines; <c>search</c> is Turkish-insensitive, every word must match. Ordered by name.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.ExaminationTemplates.Read)]
    [ProducesResponseType(typeof(PagedResult<ExaminationTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = PageRequestQuery.BindFromQuery(Request.Query, page);
        var result = await _mediator.Send(new GetExaminationTemplatesListQuery(paging, includeInactive), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.ExaminationTemplates.Read)]
    [ProducesResponseType(typeof(ExaminationTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetExaminationTemplateByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.ExaminationTemplates.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ExaminationTemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateExaminationTemplateCommand cmd, CancellationToken ct)
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

    /// <summary>Full update: name, texts and lines are replaced together; <c>lines</c> is required (empty = no lines).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.ExaminationTemplates.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ExaminationTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateExaminationTemplateBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("ExaminationTemplates.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        var result = await _mediator.Send(
            new UpdateExaminationTemplateCommand(
                id, body.Name, body.VisitReason, body.Findings, body.Plan, body.Lines!, body.RowVersion),
            ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.ExaminationTemplates.Deactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SetExaminationTemplateActiveCommand(id, IsActive: false), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.ExaminationTemplates.Deactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new SetExaminationTemplateActiveCommand(id, IsActive: true), ct);
        return result.ToActionResult(this);
    }
}

/// <summary>PUT /api/v1/examination-templates/{id} body; route id is source of truth.</summary>
public sealed class UpdateExaminationTemplateBody
{
    /// <summary>Optional; when set must match route id.</summary>
    public Guid? Id { get; init; }

    public string Name { get; init; } = default!;

    public string? VisitReason { get; init; }

    public string? Findings { get; init; }

    public string? Plan { get; init; }

    /// <summary>Required; replaces all lines (empty list = text-only template).</summary>
    public IReadOnlyList<ExaminationTemplateLineRequest>? Lines { get; init; }

    /// <summary>Base64 version from GET/list; required.</summary>
    public string? RowVersion { get; init; }
}
