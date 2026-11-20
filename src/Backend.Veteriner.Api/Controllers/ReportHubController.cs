using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Hub;
using Backend.Veteriner.Application.Reports.Hub.Queries;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Rapor Merkezi ana ekran özeti (ADR-052). Sözleşme: <c>docs/REPORT_HUB_SUMMARY_API_CONTRACT.md</c>. Uç tek izne bağlı değildir;
/// her bölüm aynı veriyi gösteren raporun iznine göre dolar ya da <c>null</c> kalır (yeni veri açılmaz).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports")]
[Produces("application/json")]
[Authorize]
public sealed class ReportHubController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ReportHubController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <param name="period"><c>today</c> | <c>week</c> | <c>month</c>.</param>
    /// <param name="clinicId">Boşsa aktif klinik bağlamı; ikisi de yoksa 400 <c>ReportsHub.ClinicScopeRequired</c>.</param>
    [HttpGet("hub-summary")]
    [ProducesResponseType(typeof(ReportHubSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHubSummary(
        [FromQuery] string? period,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetReportHubSummaryQuery(period, clinicId), ct);
        return result.ToActionResult(this);
    }
}
