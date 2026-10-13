using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.StockBatches.Contracts.Dtos;
using Backend.Veteriner.Application.StockBatches.Queries.GetById;
using Backend.Veteriner.Application.StockBatches.Queries.GetList;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/stock-batches")]
[Produces("application/json")]
[Authorize]
public sealed class StockBatchesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public StockBatchesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Klinik kapsamı zorunlu: <c>clinicId</c> veya aktif klinik bağlamı. Varsayılan yalnız kalanı olan partiler (<c>availableOnly</c>);
    /// <c>expired</c> true yalnız süresi geçmiş, false yalnız geçmemiş. Sıralama FEFO (SKT artan, SKT'siz sonda).
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(PagedResult<StockBatchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] string? lotNumber = null,
        [FromQuery] bool availableOnly = true,
        [FromQuery] bool? expired = null,
        [FromQuery] DateOnly? expiresBeforeDate = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetStockBatchesListQuery(page, pageSize, clinicId, productId, lotNumber, availableOnly, expired, expiresBeforeDate),
            ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(StockBatchDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetStockBatchByIdQuery(id), ct);
        return result.ToActionResult(this);
    }
}
