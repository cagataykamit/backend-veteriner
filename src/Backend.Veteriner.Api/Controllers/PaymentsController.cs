using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Commands.Create;
using Backend.Veteriner.Application.Payments.Commands.Update;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Application.Payments.Queries.GetById;
using Backend.Veteriner.Application.Payments.Queries.GetList;
using Backend.Veteriner.Domain.Payments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Tahsilat kayıtları (fatura değil). Kiracı yalnızca <see cref="ITenantContext"/> ile çözülür.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
[Produces("application/json")]
[Authorize]
public sealed class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public PaymentsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Payments.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreatePaymentCommand cmd, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var id = result.Value;
        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Payments.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdatePaymentBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != id)
            return Result.Failure("Payments.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var cmd = new UpdatePaymentCommand(
            id,
            body.ClinicId,
            body.ClientId,
            body.PetId,
            body.AppointmentId,
            body.ExaminationId,
            body.Amount,
            body.Currency,
            body.Method,
            body.PaidAtUtc,
            body.Notes);

        var result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(PaymentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPaymentByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(PagedResult<PaymentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    /// <summary>
    /// Filtreler: <c>clinicId</c>, <c>clientId</c>, <c>petId</c>, <c>method</c>, <c>paidFromUtc</c>, <c>paidToUtc</c> (ödeme zamanı UTC).
    /// Metin araması: <c>search</c> — müşteri ad/e-posta/telefon; hayvan adı, tür adı, serbest ırk ve katalog ırk (hayvan listesi ile aynı LIKE kümesi); ödeme <c>notes</c>; <c>currency</c> (ISO); tümü OR, diğer filtrelerle AND.
    /// </summary>
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] PaymentMethod? method = null,
        [FromQuery] DateTime? paidFromUtc = null,
        [FromQuery] DateTime? paidToUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var paging = new PaymentListPagingRequest { Page = page, PageSize = pageSize };
        var result = await _mediator.Send(
            new GetPaymentsListQuery(paging, clinicId, clientId, petId, method, paidFromUtc, paidToUtc, search),
            ct);
        return result.ToActionResult(this);
    }
}
