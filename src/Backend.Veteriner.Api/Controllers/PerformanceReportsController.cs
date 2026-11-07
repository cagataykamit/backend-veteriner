using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Performance;
using Backend.Veteriner.Application.Reports.Performance.Breakdowns;
using Backend.Veteriner.Application.Reports.Performance.ClinicsOverview;
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

    /// <summary>
    /// Gelişmiş raporlar (TIER-004; <c>docs/PERFORMANCE_BREAKDOWNS_API_CONTRACT.md</c>): hekim, satır türü, kalem, tür ve geliş, önceki dönemle.
    /// 403 <c>Subscriptions.FeatureNotInPlan</c>: pakette yok. Bölümler özetle aynı izin kuralıyla dolar.
    /// </summary>
    [HttpGet("performance-breakdowns")]
    [ProducesResponseType(typeof(PerformanceBreakdownsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPerformanceBreakdowns(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool compare = true,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPerformanceBreakdownsQuery(from, to, clinicId, compare), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Patron ekranı (TIER-007; <c>docs/CLINICS_OVERVIEW_API_CONTRACT.md</c>): erişilebilen tüm aktif şubeler, şube başına ve toplam, önceki dönemle.
    /// Klinik filtresi yoktur; aktif klinik bağlamı kullanılmaz. 403 <c>Subscriptions.FeatureNotInPlan</c>: pakette yok.
    /// </summary>
    [HttpGet("clinics-overview")]
    [ProducesResponseType(typeof(ClinicsOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetClinicsOverview(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool compare = true,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClinicsOverviewQuery(from, to, compare), ct);
        return result.ToActionResult(this);
    }
}
