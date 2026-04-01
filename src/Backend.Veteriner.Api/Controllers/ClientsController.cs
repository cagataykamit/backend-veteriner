using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Commands.Update;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Queries.GetById;
using Backend.Veteriner.Application.Clients.Queries.GetList;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Müşteri (hayvan sahibi) yönetimi. Kiracı JWT <c>tenant_id</c>; handler’da <see cref="ITenantContext"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clients")]
[Produces("application/json")]
[Authorize]
public sealed class ClientsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClientsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Clients.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClientCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateClientCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var dto = result.Value!;
        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id = dto.Id
            },
            dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clients.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateClientCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (cmd.Id != Guid.Empty && id != cmd.Id)
            return Result.Failure("Clients.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        cmd = cmd with { Id = id };

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(ClientDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClientByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Sayfalı müşteri listesi. <c>search</c> (veya <c>page.search</c>) ad, e-posta ve telefon alanlarında arar. <c>sort</c>/<c>order</c> işlenmez.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(PagedResult<ClientListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClientsListQuery(PageRequestQuery.WithMergedSearch(page, search)), ct);
        return result.ToActionResult(this);
    }
}
