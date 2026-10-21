using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Commands.Create;
using Backend.Veteriner.Application.Charges.Commands.Correct;
using Backend.Veteriner.Application.Charges.Commands.Void;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Charges.Queries.GetAllocations;
using Backend.Veteriner.Application.Charges.Queries.GetById;
using Backend.Veteriner.Application.Charges.Queries.GetList;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Ücret (borç) satırları; tahsilat değildir. Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/charges")]
[Produces("application/json")]
[Authorize]
public sealed class ChargesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ChargesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Ücret satırı ekler. Yeni kayıt 201; aynı <c>requestId</c> ile tekrar istek 200 ve <c>created: false</c>.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Charges.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChargeCreateResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ChargeCreateResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateChargeBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CreateChargeCommand(
                body.ClinicId,
                body.ClientId,
                body.PetId,
                body.VisitId,
                body.ExaminationId,
                body.Description,
                body.Quantity,
                body.UnitPrice,
                body.RequestId,
                body.Tax),
            ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var created = result.Value!;
        if (!created.Created)
            return Ok(created);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id = created.Charge.Id },
            created);
    }

    /// <summary>
    /// Ücreti düzeltir (açıklama, miktar, birim fiyat). Yönetici düzeyi; gerekçe zorunlu. Yeni tutar tahsis edilen tutarın altına inemez.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Charges.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ChargeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Correct([FromRoute] Guid id, [FromBody] CorrectChargeBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CorrectChargeCommand(id, body.Description, body.Quantity, body.UnitPrice, body.Reason, body.Tax),
            ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Ücreti iptal eder (silmez). Yönetici düzeyi; gerekçe zorunlu. Ödemeyle karşılanmış ücret önce ödeme iptal edilmeden iptal edilemez.
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = PermissionCatalog.Charges.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Void([FromRoute] Guid id, [FromBody] VoidRecordBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new VoidChargeCommand(id, body.Reason), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(ChargeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetChargeByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Ücretin ödemelerden tahsisleri (hangi tahsilattan ne kadar).</summary>
    [HttpGet("{id:guid}/allocations")]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ChargeAllocationItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllocations([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetChargeAllocationsQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik kapsamı zorunlu: <c>clinicId</c> veya aktif klinik bağlamı. Sıralama: oluşturulma zamanı azalan.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(PagedResult<ChargeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? visitId = null,
        [FromQuery] Guid? examinationId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetChargesListQuery(page, pageSize, clinicId, clientId, petId, visitId, examinationId),
            ct);
        return result.ToActionResult(this);
    }
}
