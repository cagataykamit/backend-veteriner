using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.CashRegisters;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Registers;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record CreateCashRegisterBody(Guid? ClinicId, string? Code, string? Name);

public sealed record UpdateCashRegisterBody(string? Code, string? Name, bool IsActive);

/// <summary>
/// Kasa tanımları ve kasa hareketleri raporu (TIER-008; docs/CASH_REGISTERS_API_CONTRACT.md). Okuma her pakette; tanım Profesyonel ve
/// Kurumsal ister (403 <c>Subscriptions.FeatureNotInPlan</c>).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cash-registers")]
[Produces("application/json")]
[Authorize]
public sealed class CashRegistersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public CashRegistersController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Kliniğin kasaları; Ana kasa önce. Varsayılan yalnız aktifler.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<CashRegisterDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList([FromQuery] Guid? clinicId = null, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetCashRegistersQuery(clinicId, includeInactive), ct)).ToActionResult(this);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCashRegisterBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new CreateCashRegisterCommand(body.ClinicId, body.Code, body.Name), ct)).ToActionResult(this);
    }

    /// <summary>Ad, kod ve durum. Ana kasa pasife alınamaz (409 <c>CashRegisters.MainRegisterLocked</c>).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateCashRegisterBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new UpdateCashRegisterCommand(id, body.Code, body.Name, body.IsActive), ct)).ToActionResult(this);
    }

    /// <summary>Kasa hareketleri: devreden bakiye, dönemin hareketleri, dönem sonu bakiye. Kasa verilmezse klinikteki tüm kasalar.</summary>
    [HttpGet("report")]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Read)]
    [ProducesResponseType(typeof(CashRegisterReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReport(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? cashRegisterId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        return (await _mediator.Send(new GetCashRegisterReportQuery(from, to, clinicId, cashRegisterId), ct)).ToActionResult(this);
    }

    /// <summary>Kasa hareketleri CSV (devreden ve dönem sonu satırlarıyla; ekranla aynı veri).</summary>
    [HttpGet("report/export")]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportReport(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? cashRegisterId = null,
        CancellationToken ct = default)
        => SendExportAsync(new ExportCashRegisterReportQuery(from, to, clinicId, cashRegisterId, RegisterExportFormat.Csv), ReportFileContentTypes.CsvUtf8, ct);

    /// <summary>Kasa hareketleri Excel. Filtreler CSV ile aynı.</summary>
    [HttpGet("report/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.CashRegisters.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportReportXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? cashRegisterId = null,
        CancellationToken ct = default)
        => SendExportAsync(new ExportCashRegisterReportQuery(from, to, clinicId, cashRegisterId, RegisterExportFormat.Xlsx), ReportFileContentTypes.Xlsx, ct);

    private async Task<IActionResult> SendExportAsync(IRequest<Backend.Veteriner.Domain.Shared.Result<RegisterExportResult>> query, string contentType, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, contentType, v.FileDownloadName);
    }
}
