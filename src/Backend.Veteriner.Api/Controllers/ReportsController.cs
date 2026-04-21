using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;
using Backend.Veteriner.Application.Reports.Appointments.Queries.GetAppointmentReport;
using Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;
using Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Payments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Panel raporlama uçları (dashboard’dan ayrıdır). Ödeme ve randevu raporları.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports")]
[Produces("application/json")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ReportsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Tarih aralıklı ödeme raporu (sayfalı). <c>from</c>/<c>to</c> UTC — <see cref="Payment.PaidAtUtc"/> [from,to] dahil.
    /// </summary>
    [HttpGet("payments")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos.PaymentReportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPaymentsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] PaymentMethod? method = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new GetPaymentsReportQuery(from, to, clinicId, method, clientId, petId, search, page, pageSize);
        var result = await _mediator.Send(query, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Klinik içi tahsilat raporu CSV (sayfa yok; UTF-8 BOM; ayırıcı <c>;</c>, Excel TR). Tarih/saat Europe/Istanbul; teknik ID kolonları yok.
    /// Aynı filtre semantiği JSON rapor ile aynıdır.
    /// </summary>
    [HttpGet("payments/export")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportPaymentsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] PaymentMethod? method = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportPaymentsReportQuery(from, to, clinicId, method, clientId, petId, search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        const string contentType = "text/csv; charset=utf-8";
        return File(v.ContentUtf8Bom, contentType, v.FileDownloadName);
    }

    /// <summary>
    /// Klinik içi tahsilat raporu Excel (açılışta tarih/tutar hücre biçimi). Filtreler CSV ile aynı; sayfa yok.
    /// </summary>
    [HttpGet("payments/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportPaymentsReportXlsx(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] PaymentMethod? method = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportPaymentsReportXlsxQuery(from, to, clinicId, method, clientId, petId, search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return File(v.Content, contentType, v.FileDownloadName);
    }

    /// <summary>
    /// Randevu raporu (sayfalı). <c>from</c>/<c>to</c> UTC — <c>ScheduledAtUtc</c> <c>[from,to]</c> dahil.
    /// </summary>
    [HttpGet("appointments")]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos.AppointmentReportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAppointmentsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] AppointmentStatus? status = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new GetAppointmentsReportQuery(from, to, clinicId, status, clientId, petId, search, page, pageSize);
        var result = await _mediator.Send(query, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Randevu raporu CSV (UTF-8 BOM; ayırıcı <c>;</c>). Filtreler JSON ile aynı.</summary>
    [HttpGet("appointments/export")]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportAppointmentsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] AppointmentStatus? status = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportAppointmentsReportQuery(from, to, clinicId, status, clientId, petId, search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        const string contentType = "text/csv; charset=utf-8";
        return File(v.ContentUtf8Bom, contentType, v.FileDownloadName);
    }

    [HttpGet("appointments/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportAppointmentsReportXlsx(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] AppointmentStatus? status = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportAppointmentsReportXlsxQuery(from, to, clinicId, status, clientId, petId, search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        const string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return File(v.Content, contentType, v.FileDownloadName);
    }
}
