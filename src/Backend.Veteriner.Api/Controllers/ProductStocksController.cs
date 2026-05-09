using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.ProductStocks.Commands.UpdateMinimumStockLevel;
using Backend.Veteriner.Application.ProductStocks.Contracts.Dtos;
using Backend.Veteriner.Application.ProductStocks.Queries.GetList;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/product-stocks")]
[Produces("application/json")]
[Authorize]
public sealed class ProductStocksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ProductStocksController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(PagedResult<ProductStockDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] PageRequest page,
        [FromQuery] Guid? clinicId,
        [FromQuery] Guid? productCategoryId,
        [FromQuery] Guid? productId,
        [FromQuery] bool? isBelowMinimum,
        [FromQuery] bool? isActiveProduct,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetProductStocksListQuery(page, clinicId, productCategoryId, productId, isBelowMinimum, isActiveProduct),
            ct);

        return result.ToActionResult(this);
    }

    [HttpPut("{id:guid}/minimum-stock-level")]
    [Authorize(Policy = PermissionCatalog.Products.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ProductStockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMinimumStockLevel(
        [FromRoute] Guid id,
        [FromBody] UpdateProductStockMinimumStockLevelRequest body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new UpdateProductStockMinimumStockLevelCommand(id, body.MinimumStockLevel),
            ct);

        return result.ToActionResult(this);
    }
}
