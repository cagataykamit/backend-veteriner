using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.GoodsReceipts.Commands.Create;
using Backend.Veteriner.Application.GoodsReceipts.Contracts.Dtos;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/goods-receipts")]
[Produces("application/json")]
[Authorize]
public sealed class GoodsReceiptsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public GoodsReceiptsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Mal kabul: çok satırlı stok girişi, hepsi ya da hiçbiri. Yeni kayıt 201; aynı <c>requestId</c> ve içerikle tekrar istek 200 ve <c>created: false</c>.
    /// İş kuralı hata iletisi <c>Satır n:</c> ile başlar; doğrulama hataları <c>lines[i].alan</c> anahtarını taşır.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.StockMovements.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(GoodsReceiptCreateResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GoodsReceiptCreateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateGoodsReceiptCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        return created.Created ? StatusCode(StatusCodes.Status201Created, created) : Ok(created);
    }
}
