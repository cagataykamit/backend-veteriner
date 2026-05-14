using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.VaccineDefinitions.Commands.Activate;
using Backend.Veteriner.Application.VaccineDefinitions.Commands.Create;
using Backend.Veteriner.Application.VaccineDefinitions.Commands.Deactivate;
using Backend.Veteriner.Application.VaccineDefinitions.Commands.Update;
using Backend.Veteriner.Application.VaccineDefinitions.Contracts.Dtos;
using Backend.Veteriner.Application.VaccineDefinitions.Queries.GetById;
using Backend.Veteriner.Application.VaccineDefinitions.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vaccine-definitions")]
[Produces("application/json")]
[Authorize]
public sealed class VaccineDefinitionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public VaccineDefinitionsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Read)]
    [ProducesResponseType(typeof(PagedResult<VaccineDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        [FromQuery] Guid? speciesId = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var merged = new PageRequest
        {
            Page = page.Page,
            PageSize = page.PageSize,
            Sort = page.Sort,
            Order = page.Order,
            Search = search ?? page.Search,
        };

        var result = await _mediator.Send(new GetVaccineDefinitionsListQuery(merged, speciesId, includeInactive), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Read)]
    [ProducesResponseType(typeof(VaccineDefinitionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetVaccineDefinitionByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateVaccineDefinitionCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = result.Value },
            result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateVaccineDefinitionCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (cmd.Id != Guid.Empty && cmd.Id != id)
            return Result.Failure("VaccineDefinitions.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var result = await _mediator.Send(cmd with { Id = id }, ct);
        return result.ToActionResult(this);
    }

    [HttpPatch("{id:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ActivateVaccineDefinitionCommand(id), ct);
        return result.ToActionResult(this);
    }

    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.VaccineDefinitions.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new DeactivateVaccineDefinitionCommand(id), ct);
        return result.ToActionResult(this);
    }
}
