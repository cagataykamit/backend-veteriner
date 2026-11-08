using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.DataExports;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Kiracının tüm verisini tek ZIP pakette dışa aktarma (docs/DATA_EXPORT_API_CONTRACT.md). Yalnız <c>DataExport.Manage</c>
/// (Admin, Owner); her pakette ve salt okunur abonelikte açık. Paket arka planda hazırlanır; indirme yalnız kimlikli istekle
/// (imzalı/kimliksiz bağlantı yok). Talep ve indirme denetim kaydına yazılır.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/data-exports")]
[Produces("application/json")]
[Authorize(Policy = PermissionCatalog.DataExport.Manage)]
public sealed class DataExportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public DataExportsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Son işler ve talep kuralı (kalan talep, açık iş).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DataExportOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetDataExportsQuery(), ct)).ToActionResult(this);
    }

    /// <summary>Yeni paket talebi: 202 ve kuyruktaki iş. Açık iş varsa ya da talep sınırı doluysa 409.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(DataExportJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new RequestDataExportCommand(), ct);
        return result.IsSuccess ? Accepted(result.Value) : result.ToActionResult(this);
    }

    /// <summary>Hazır paketi indirir (<c>application/zip</c>). Hazır değilse 409, süresi dolduysa 410.</summary>
    [HttpGet("{id:guid}/download")]
    [Produces("application/zip")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public async Task<IActionResult> Download([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new DownloadDataExportQuery(id), ct)).ToFileResult(this, download: true);
    }
}
