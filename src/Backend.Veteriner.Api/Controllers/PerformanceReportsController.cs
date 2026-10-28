using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Performance;
using Backend.Veteriner.Application.Reports.Performance.Queries;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// İşletme performansı özeti (REPORT-008). Sözleşme: <c>docs/PERFORMANCE_SUMMARY_API_CONTRACT.md</c>. Uç tek izne bağlı değildir;
/// her bölüm ilgili raporun iznine göre dolar ya da <c>null</c> kalır (yeni veri açılmaz).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports")]
[Produces("application/json")]
[Authorize]
public sealed class PerformanceReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public PerformanceReportsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet("performance-summary")]
    [ProducesResponseType(typeof(PerformanceSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPerformanceSummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool compare = true,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPerformanceSummaryQuery(from, to, clinicId, compare), ct);
        return result.ToActionResult(this);
    }
}
