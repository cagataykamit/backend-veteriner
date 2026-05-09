using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.StockMovements.Commands.Create;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Application.StockMovements.Queries.GetList;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/stock-movements")]
[Produces("application/json")]
[Authorize]
public sealed class StockMovementsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public StockMovementsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(PagedResult<StockMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] Guid? clinicId,
        [FromQuery] Guid? productId,
        [FromQuery] Guid? productCategoryId,
        [FromQuery] StockMovementType? movementType,
        [FromQuery] DateTime? dateFromUtc,
        [FromQuery] DateTime? dateToUtc,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetStockMovementsListQuery(
                page,
                clinicId,
                productId,
                productCategoryId,
                movementType,
                dateFromUtc,
                dateToUtc),
            ct);

        return result.ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.StockMovements.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(StockMovementDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateStockMovementCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(GetList),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0" },
            result.Value);
    }
}
