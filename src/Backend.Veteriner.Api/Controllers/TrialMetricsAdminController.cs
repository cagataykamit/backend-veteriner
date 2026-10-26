using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Queries.GetTrialActivationMetrics;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Deneme aktivasyon ölçümleri (TRIAL-008): yalnız platform yöneticisi (<c>platform_admin</c> claim'i); klinik rolleri 403.
/// Sözleşme: <c>docs/TRIAL_ACTIVATION_METRICS_API_CONTRACT.md</c> §3.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/trial-metrics")]
[Authorize(Policy = AuthorizationPolicyNames.PlatformAdmin)]
[Produces("application/json")]
public sealed class TrialMetricsAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public TrialMetricsAdminController(IMediator mediator) => _mediator = mediator;

    /// <param name="fromUtc">Kohort başlangıcı (dahil); yoksa <c>toUtc − 30 gün</c>.</param>
    /// <param name="toUtc">Kohort bitişi (hariç); yoksa istek anı.</param>
    [HttpGet]
    [ProducesResponseType(typeof(TrialActivationMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTrialActivationMetricsQuery(fromUtc, toUtc), ct);
        return result.ToActionResult(this);
    }
}
