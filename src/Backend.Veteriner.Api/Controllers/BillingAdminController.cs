using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Tenants.Commands.Billing;
using Backend.Veteriner.Application.Tenants.Queries.GetDuplicatePayments;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Çift ödemeler ve iadeleri (BILL-005): yalnız platform yöneticisi (<c>platform_admin</c> claim'i); klinik rolleri 403.
/// Sözleşme: <c>docs/BACKEND-CONTRACT-STANDARD.md</c> (Abonelik checkout, çift ödeme).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/billing")]
[Authorize(Policy = AuthorizationPolicyNames.PlatformAdmin)]
[PlatformTenantScope]
[Produces("application/json")]
public sealed class BillingAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public BillingAdminController(IMediator mediator) => _mediator = mediator;

    [HttpGet("duplicate-payments")]
    [ProducesResponseType(typeof(IReadOnlyList<DuplicatePaymentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDuplicatePayments(CancellationToken ct)
        => Ok(await _mediator.Send(new GetDuplicatePaymentsQuery(), ct));

    /// <summary>Otomatik iadesi başarısız çift ödeme sağlayıcı panelinden iade edildi.</summary>
    [HttpPost("duplicate-payments/{checkoutSessionId:guid}/mark-refunded")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkRefunded([FromRoute] Guid checkoutSessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new MarkDuplicatePaymentRefundedCommand(checkoutSessionId), ct);
        return result.IsSuccess ? NoContent() : result.ToActionResult(this);
    }
}
