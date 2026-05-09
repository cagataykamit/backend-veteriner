using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Products.Commands.Activate;
using Backend.Veteriner.Application.Products.Commands.Create;
using Backend.Veteriner.Application.Products.Commands.Deactivate;
using Backend.Veteriner.Application.Products.Commands.Update;
using Backend.Veteriner.Application.Products.Contracts.Dtos;
using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Application.ProductStocks.Queries.GetByProductId;
using Backend.Veteriner.Application.StockMovements.Contracts.Dtos;
using Backend.Veteriner.Application.StockMovements.Queries.GetByProductId;
using Backend.Veteriner.Application.Products.Queries.GetById;
using Backend.Veteriner.Application.Products.Queries.GetList;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
[Produces("application/json")]
[Authorize]
public sealed class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ProductsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] Guid? productCategoryId,
        [FromQuery] bool? isActive,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetProductsListQuery(page, productCategoryId, isActive), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetProductByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}/stocks")]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ProductStockDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStocksByProductId([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetProductStocksByProductIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}/stock-movements")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(PagedResult<StockMovementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStockMovementsByProductId(
        [FromRoute] Guid id,
        [FromQuery] PageRequest page,
        [FromQuery] Guid? clinicId,
        [FromQuery] StockMovementType? movementType,
        [FromQuery] DateTime? dateFromUtc,
        [FromQuery] DateTime? dateToUtc,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetStockMovementsByProductIdQuery(id, page, clinicId, movementType, dateFromUtc, dateToUtc),
            ct);

        return result.ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Products.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(GetById),
            new
            {
                version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0",
                id = result.Value!.Id
            },
            result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Products.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateProductCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (cmd.Id != Guid.Empty && cmd.Id != id)
            return Result.Failure("Products.RouteIdMismatch", "Route id ile body id uyuşmuyor.").ToActionResult(this);

        var result = await _mediator.Send(cmd with { Id = id }, ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.Products.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ActivateProductCommand(id), ct);
        return result.ToActionResult(this);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.Products.Deactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new DeactivateProductCommand(id), ct);
        return result.ToActionResult(this);
    }
}
