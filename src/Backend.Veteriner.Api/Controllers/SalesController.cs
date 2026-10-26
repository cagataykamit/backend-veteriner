using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Sales.Commands.Create;
using Backend.Veteriner.Application.Sales.Commands.EnsureWalkInClient;
using Backend.Veteriner.Application.Sales.Commands.Void;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Application.Sales.Queries.GetById;
using Backend.Veteriner.Application.Sales.Queries.GetList;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Muayenesiz ürün satışı (ADR-016): oluşturma stok düşümünü, tek ücreti ve ödemeyi, iptal ödeme/ücret iptalini ve stok iadesini
/// aynı transaction'da yapar. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sales")]
[Produces("application/json")]
[Authorize]
public sealed class SalesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public SalesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Satışı kaydeder. Yeni kayıt 201; aynı <c>requestId</c> ve içerikle tekrar istek 200 ve <c>created: false</c>.
    /// Müşteri verilmezse Peşin müşteri (ödeme zorunlu, tam tutar). Aşı ve narkotik/psikotropik ürün satılamaz.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Sales.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SaleCreateResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(SaleCreateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateSaleBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CreateSaleCommand(body.ClinicId, body.ClientId, body.RequestId, body.Notes, body.Lines, body.Payment),
            ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (!created.Created)
            return Ok(created);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.RequestedApiVersion?.ToString() ?? "1.0", id = created.Sale.Id },
            created);
    }

    /// <summary>
    /// Satışı tam iptal eder (silmez). Gerekçe zorunludur (5-500 karakter); <c>stockOutcome</c>: <c>StockReturned</c> (varsayılan, ters hareketle stok iadesi) veya <c>WrittenOff</c> (zarar/imha, stok artmaz).
    /// Satışın ödemesi ve ücreti de iptal edilir; ücrete sonradan başka ödeme tahsis edilmişse 409 <c>Charges.VoidBlockedByAllocation</c>.
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = PermissionCatalog.Sales.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SaleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void([FromRoute] Guid id, [FromBody] VoidSaleBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new VoidSaleCommand(id, body.Reason, body.StockOutcome), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Peşin müşteriyi getirir, yoksa oluşturur (idempotent).</summary>
    [HttpPost("walk-in-client")]
    [Authorize(Policy = PermissionCatalog.Sales.Create)]
    [ProducesResponseType(typeof(WalkInClientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EnsureWalkInClient(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new EnsureWalkInClientCommand(), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Sales.Read)]
    [ProducesResponseType(typeof(SaleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetSaleByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik kapsamı zorunlu: <c>clinicId</c> veya aktif klinik bağlamı. Sıralama: satış zamanı azalan; <c>toUtc</c> hariç; iptaller varsayılan gizli.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Sales.Read)]
    [ProducesResponseType(typeof(PagedResult<SaleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetSalesListQuery(page, pageSize, clinicId, clientId, productId, fromUtc, toUtc, includeVoided),
            ct);
        return result.ToActionResult(this);
    }
}
