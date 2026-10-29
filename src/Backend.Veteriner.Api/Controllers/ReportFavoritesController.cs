using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Preferences.Favorites;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Rapor Merkezi favorileri (REPORT-003). Sözleşme: <c>docs/REPORT_PREFERENCES_API_CONTRACT.md</c>. Kayıtlar oturumdaki
/// kullanıcıya ve kiracıya aittir; rapor verisi açmadığı için ayrı izin yoktur (raporun görünürlüğü panelde izne bağlıdır).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports/favorites")]
[Produces("application/json")]
[Authorize]
public sealed class ReportFavoritesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ReportFavoritesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReportFavoriteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetMyReportFavoritesQuery(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Favoriye ekler; zaten favorideyse yine 204.</summary>
    [HttpPut("{reportKey}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Add([FromRoute] string reportKey, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new AddReportFavoriteCommand(reportKey), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Favoriden çıkarır; favoride değilse yine 204.</summary>
    [HttpDelete("{reportKey}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Remove([FromRoute] string reportKey, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new RemoveReportFavoriteCommand(reportKey), ct);
        return result.ToActionResult(this);
    }
}
