using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Queries.GetCapabilities;
using Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;
using Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;
using Backend.Veteriner.Application.Dashboard.Queries.GetSummary;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik paneli özeti. Kiracı <see cref="ITenantContext"/> ile çözülür (JWT / sorgu).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Produces("application/json")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public DashboardController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>UTC takvim günü ve anına göre özet metrikler; ayrıntılar için DTO açıklamalarına bakın.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = PermissionCatalog.Dashboard.Read)]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetDashboardSummaryQuery(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Bugün / bu hafta / bu ay tahsilat toplamları ve sayıları (İstanbul takvim pencereleri) ile son ödemeler (aktif klinik bağlamı varsa bu klinik).
    /// </summary>
    [HttpGet("finance-summary")]
    [Authorize(Policy = PermissionCatalog.Dashboard.Read)]
    [ProducesResponseType(typeof(DashboardFinanceSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFinanceSummary(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetDashboardFinanceSummaryQuery(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Dashboard widget görünürlüğü için rol/permission ve context tabanlı capability özeti.
    /// </summary>
    [HttpGet("capabilities")]
    [Authorize(Policy = PermissionCatalog.Dashboard.Read)]
    [ProducesResponseType(typeof(DashboardCapabilitiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCapabilities(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetDashboardCapabilitiesQuery(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Dashboard operasyonel uyarı sayaçları (tenant/clinic scope).
    /// </summary>
    [HttpGet("operational-alerts")]
    [Authorize(Policy = PermissionCatalog.Dashboard.Read)]
    [ProducesResponseType(typeof(DashboardOperationalAlertsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOperationalAlerts(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetDashboardOperationalAlertsQuery(), ct);
        return result.ToActionResult(this);
    }
}
