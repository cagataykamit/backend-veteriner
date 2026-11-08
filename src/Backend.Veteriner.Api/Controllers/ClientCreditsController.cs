using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.ClientCredits;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Payments;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Müşteri kredisi: liste, mahsup, mahsubu geri alma, iade (FIN-005; docs/CLIENT_CREDIT_API_CONTRACT.md). Kredi ödeme içinde doğar
/// (<c>POST /payments</c> <c>creditAmount</c>). Yeni izin yoktur: okuma <c>Charges.Read</c>, mahsup <c>Payments.Create</c>,
/// geri alma ve iade <c>Payments.Correct</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clients/{clientId:guid}/credits")]
[Produces("application/json")]
[Authorize]
public sealed class ClientCreditsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClientCreditsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Müşterinin kredileri; <c>includeClosed=false</c> (varsayılan) yalnız kalanı olanlar.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<ClientCreditDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetList(
        [FromRoute] Guid clientId,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeClosed = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClientCreditsQuery(clientId, clinicId, includeClosed), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Krediden ücretlere mahsup (en eski kredi önce).</summary>
    [HttpPost("apply")]
    [Authorize(Policy = PermissionCatalog.Payments.Create)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClientCreditApplyResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Apply([FromRoute] Guid clientId, [FromBody] ApplyClientCreditBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(
            new ApplyClientCreditCommand(clientId, body.ClinicId, body.RequestId, body.Allocations ?? []), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Mahsubu geri alır; gerekçe zorunlu.</summary>
    [HttpPost("applications/{applicationId:guid}/reverse")]
    [Authorize(Policy = PermissionCatalog.Payments.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reverse(
        [FromRoute] Guid clientId,
        [FromRoute] Guid applicationId,
        [FromBody] ClientCreditReasonBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new ReverseClientCreditApplicationCommand(clientId, applicationId, body.Reason), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kalan kredinin tamamını iade eder (kasa çıkışı).</summary>
    [HttpPost("{creditId:guid}/refund")]
    [Authorize(Policy = PermissionCatalog.Payments.Correct)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClientCreditDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Refund(
        [FromRoute] Guid clientId,
        [FromRoute] Guid creditId,
        [FromBody] RefundClientCreditBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new RefundClientCreditCommand(clientId, creditId, body.Reason, body.Method, body.CashRegisterId), ct);
        return result.ToActionResult(this);
    }
}

public sealed record ApplyClientCreditBody(Guid? ClinicId, Guid RequestId, IReadOnlyList<ClientCreditApplyLineDto>? Allocations);

public sealed record ClientCreditReasonBody(string? Reason);

public sealed record RefundClientCreditBody(
    string? Reason,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod Method,
    Guid? CashRegisterId = null);
