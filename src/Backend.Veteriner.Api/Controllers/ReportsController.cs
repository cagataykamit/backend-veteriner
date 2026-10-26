using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments.Queries.ExportAppointmentReport;
using Backend.Veteriner.Application.Reports.Appointments.Queries.GetAppointmentReport;
using Backend.Veteriner.Application.Reports.Examinations.Queries.ExportExaminationReport;
using Backend.Veteriner.Application.Reports.Examinations.Queries.GetExaminationReport;
using Backend.Veteriner.Application.Reports.Vaccinations.Queries.ExportVaccinationReport;
using Backend.Veteriner.Application.Reports.Vaccinations.Queries.GetVaccinationReport;
using Backend.Veteriner.Application.Reports.Payments.Queries.ExportPaymentReport;
using Backend.Veteriner.Application.Reports.Payments.Queries.GetPaymentReport;
using Backend.Veteriner.Application.Reports.OutstandingBalances;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.ExportOutstandingBalancesReport;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Queries.GetOutstandingBalancesReport;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;
using Backend.Veteriner.Application.Reports.Registers.Examinations;
using Backend.Veteriner.Application.Reports.Registers.Hbs;
using Backend.Veteriner.Application.Reports.Registers.Medications;
using Backend.Veteriner.Application.Reports.Registers.Prescriptions;
using Backend.Veteriner.Application.Reports.Registers.VaccineInfo;
using Backend.Veteriner.Application.Reports.Registers.Vaccines;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Panel raporlama uçları (dashboard’dan ayrıdır). Ödeme, randevu, muayene, aşı ve borç raporları.</summary>
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
        return File(v.ContentUtf8Bom, ReportFileContentTypes.CsvUtf8, v.FileDownloadName);
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
        return File(v.Content, ReportFileContentTypes.Xlsx, v.FileDownloadName);
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
        return File(v.ContentUtf8Bom, ReportFileContentTypes.CsvUtf8, v.FileDownloadName);
    }

    /// <summary>Randevu raporu XLSX; filtreler CSV/JSON ile aynı; sayfa yok.</summary>
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
        return File(v.Content, ReportFileContentTypes.Xlsx, v.FileDownloadName);
    }

    /// <summary>
    /// Muayene raporu (sayfalı). <c>from</c>/<c>to</c> UTC — <c>ExaminedAtUtc</c> <c>[from,to)</c> (to hariç).
    /// </summary>
    [HttpGet("examinations")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(Backend.Veteriner.Application.Reports.Examinations.Contracts.Dtos.ExaminationReportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetExaminationsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? appointmentId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new GetExaminationsReportQuery(
            from,
            to,
            clinicId,
            clientId,
            petId,
            appointmentId,
            search,
            page,
            pageSize);
        var result = await _mediator.Send(query, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Muayene raporu CSV (UTF-8 BOM; <c>;</c>). Filtreler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("examinations/export")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportExaminationsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? appointmentId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportExaminationsReportQuery(
            from,
            to,
            clinicId,
            clientId,
            petId,
            appointmentId,
            search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.ContentUtf8Bom, ReportFileContentTypes.CsvUtf8, v.FileDownloadName);
    }

    /// <summary>Muayene raporu XLSX; filtreler CSV/JSON ile aynı; sayfa yok.</summary>
    [HttpGet("examinations/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportExaminationsReportXlsx(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] Guid? appointmentId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportExaminationsReportXlsxQuery(
            from,
            to,
            clinicId,
            clientId,
            petId,
            appointmentId,
            search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, ReportFileContentTypes.Xlsx, v.FileDownloadName);
    }

    /// <summary>
    /// Aşı raporu (sayfalı). <c>from</c>/<c>to</c> UTC kapalı aralık; rapor tarih ekseni §31.1 (Applied → <c>AppliedAtUtc</c>; Scheduled/Cancelled → <c>DueAtUtc</c>).
    /// </summary>
    [HttpGet("vaccinations")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(Backend.Veteriner.Application.Reports.Vaccinations.Contracts.Dtos.VaccinationReportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetVaccinationsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] VaccinationStatus? status = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new GetVaccinationsReportQuery(
            from, to, clinicId, status, clientId, petId, search, page, pageSize);
        var result = await _mediator.Send(query, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Aşı raporu CSV (UTF-8 BOM; <c>;</c>). Filtreler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("vaccinations/export")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportVaccinationsReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] VaccinationStatus? status = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportVaccinationsReportQuery(
            from, to, clinicId, status, clientId, petId, search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.ContentUtf8Bom, ReportFileContentTypes.CsvUtf8, v.FileDownloadName);
    }

    /// <summary>Aşı raporu XLSX; filtreler CSV/JSON ile aynı; sayfa yok.</summary>
    [HttpGet("vaccinations/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportVaccinationsReportXlsx(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] VaccinationStatus? status = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? petId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new ExportVaccinationsReportXlsxQuery(
            from, to, clinicId, status, clientId, petId, search);
        var result = await _mediator.Send(query, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, ReportFileContentTypes.Xlsx, v.FileDownloadName);
    }

    /// <summary>
    /// Borç / açık bakiye raporu (sayfalı): müşteri bazlı <b>şu anki</b> kalan tutar (tarih parametresi yok), kalan tutara göre azalan.
    /// Kural <c>GET /clients/{id}/balance</c> ile aynıdır; klinik kapsamı <c>payments</c> raporu ile aynı çözümleyicidir.
    /// </summary>
    [HttpGet("outstanding-balances")]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(OutstandingBalancesReportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOutstandingBalancesReport(
        [FromQuery] Guid? clinicId = null,
        [FromQuery] decimal? minAmount = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = OutstandingBalancesReportConstants.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var query = new GetOutstandingBalancesReportQuery(clinicId, minAmount, search, page, pageSize);
        var result = await _mediator.Send(query, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Borç raporu CSV (sayfa yok; UTF-8 BOM; ayırıcı <c>;</c>, Excel TR). Filtre ve sıralama JSON rapor ile aynıdır.</summary>
    [HttpGet("outstanding-balances/export")]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportOutstandingBalancesReport(
        [FromQuery] Guid? clinicId = null,
        [FromQuery] decimal? minAmount = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ExportOutstandingBalancesReportQuery(clinicId, minAmount, search), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.ContentUtf8Bom, ReportFileContentTypes.CsvUtf8, v.FileDownloadName);
    }

    /// <summary>Borç raporu Excel (tarih/tutar hücre biçimi). Filtreler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("outstanding-balances/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportOutstandingBalancesReportXlsx(
        [FromQuery] Guid? clinicId = null,
        [FromQuery] decimal? minAmount = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ExportOutstandingBalancesReportXlsxQuery(clinicId, minAmount, search), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, ReportFileContentTypes.Xlsx, v.FileDownloadName);
    }

    /// <summary>
    /// Muayene kayıt defteri (sayfalı). <c>from</c>/<c>to</c>: İstanbul takvim günleri, ikisi dahil (en çok 366 gün).
    /// İptal edilmiş uygulamalar <c>includeVoided=true</c> olmadıkça gelmez. Resmî biçime uygunluk doğrulanmadı.
    /// </summary>
    [HttpGet("registers/examinations")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<ExaminationRegisterItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetExaminationRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetExaminationRegisterQuery(from, to, clinicId, includeVoided, page, pageSize), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Muayene defteri CSV (UTF-8 BOM, <c>;</c>, Excel TR). Filtreler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("registers/examinations/export")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportExaminationRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ExportExaminationRegisterQuery(from, to, clinicId, includeVoided), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, ReportFileContentTypes.CsvUtf8, v.FileDownloadName);
    }

    /// <summary>Muayene defteri Excel (gruplu başlık, tarih/sayı hücre biçimi). Filtreler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("registers/examinations/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Examinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportExaminationRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ExportExaminationRegisterXlsxQuery(from, to, clinicId, includeVoided), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, ReportFileContentTypes.Xlsx, v.FileDownloadName);
    }

    /// <summary>
    /// Reçete kayıt defteri (sayfalı; sayfa ve sayım reçete bazlı). <c>from</c>/<c>to</c>: İstanbul takvim günleri, ikisi dahil
    /// (en çok 366 gün). Resmî biçime uygunluk doğrulanmadı.
    /// </summary>
    [HttpGet("registers/prescriptions")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<PrescriptionRegisterEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPrescriptionRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPrescriptionRegisterQuery(from, to, clinicId, page, pageSize), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Reçete defteri CSV (reçete satırı başına bir satır). Filtreler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("registers/prescriptions/export")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportPrescriptionRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ExportPrescriptionRegisterQuery(from, to, clinicId), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, ReportFileContentTypes.CsvUtf8, v.FileDownloadName);
    }

    /// <summary>Reçete defteri Excel (gruplu başlık, tarih/sayı hücre biçimi). Filtreler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("registers/prescriptions/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportPrescriptionRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new ExportPrescriptionRegisterXlsxQuery(from, to, clinicId), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var v = result.Value!;
        return File(v.Content, ReportFileContentTypes.Xlsx, v.FileDownloadName);
    }

    // ---- Parti (lot) verisinden türetilen defterler ve Aşı Bilgi raporu (REGISTERS_API_CONTRACT.md bölüm II) ----

    /// <summary>
    /// Aşı kayıt defteri (aşı stok girişleri, sayfalı). <c>from</c>/<c>to</c>: İstanbul takvim günleri, ikisi dahil (en çok 366 gün);
    /// tarih stok hareketinin zamanıdır. Geri alınan girişler <c>includeVoided=true</c> olmadıkça gelmez. Resmî biçime uygunluk doğrulanmadı.
    /// </summary>
    [HttpGet("registers/vaccines")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<VaccineRegisterItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetVaccineRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
        => SendRegisterAsync(new GetVaccineRegisterQuery(new RegisterQuery(from, to, clinicId, includeVoided), page, pageSize), ct);

    /// <summary>Aşı kayıt defteri CSV (UTF-8 BOM, <c>;</c>, Excel TR). Süzgeçler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("registers/vaccines/export")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportVaccineRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportVaccineRegisterQuery(new RegisterQuery(from, to, clinicId, includeVoided), RegisterExportFormat.Csv), ct);

    /// <summary>Aşı kayıt defteri Excel (tarih/sayı hücre biçimi). Süzgeçler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("registers/vaccines/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportVaccineRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportVaccineRegisterQuery(new RegisterQuery(from, to, clinicId, includeVoided), RegisterExportFormat.Xlsx), ct);

    /// <summary>
    /// İlaç kayıt defteri (ilaç stok girişleri; aşılar hariç, sayfalı). Tarih ve süzgeç kuralları aşı kayıt defteriyle aynıdır.
    /// Resmî biçime uygunluk doğrulanmadı.
    /// </summary>
    [HttpGet("registers/medications")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<MedicationRegisterItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetMedicationRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
        => SendRegisterAsync(new GetMedicationRegisterQuery(new RegisterQuery(from, to, clinicId, includeVoided), page, pageSize), ct);

    /// <summary>İlaç kayıt defteri CSV (UTF-8 BOM, <c>;</c>, Excel TR). Süzgeçler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("registers/medications/export")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportMedicationRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportMedicationRegisterQuery(new RegisterQuery(from, to, clinicId, includeVoided), RegisterExportFormat.Csv), ct);

    /// <summary>İlaç kayıt defteri Excel (tarih/sayı hücre biçimi). Süzgeçler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("registers/medications/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportMedicationRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportMedicationRegisterQuery(new RegisterQuery(from, to, clinicId, includeVoided), RegisterExportFormat.Xlsx), ct);

    /// <summary>
    /// Narkotik ve psikotropik ürünler stok ve sarf defteri (kontrollü ürünlerin stok hareketleri, hareket başına bir satır, sayfalı).
    /// Satırlar uygulama çıkışlarında hasta ve sahip adını taşıdığından <c>StockMovements.Read</c> ve <c>Administrations.Read</c>
    /// birlikte gerekir. Resmî biçime uygunluk doğrulanmadı.
    /// </summary>
    [HttpGet("registers/controlled-substances")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<ControlledSubstanceRegisterItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetControlledSubstanceRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
        => SendRegisterAsync(
            new GetControlledSubstanceRegisterQuery(new RegisterQuery(from, to, clinicId, IncludeVoided: false), page, pageSize), ct);

    /// <summary>Narkotik ve psikotropik defteri CSV (UTF-8 BOM, <c>;</c>, Excel TR; iki katmanlı başlık tek satırda <c>Grup · Sütun</c>).</summary>
    [HttpGet("registers/controlled-substances/export")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportControlledSubstanceRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportControlledSubstanceRegisterQuery(new RegisterQuery(from, to, clinicId, IncludeVoided: false), RegisterExportFormat.Csv), ct);

    /// <summary>Narkotik ve psikotropik defteri Excel (gruplu başlık, tarih/sayı hücre biçimi).</summary>
    [HttpGet("registers/controlled-substances/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.StockMovements.Read)]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportControlledSubstanceRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportControlledSubstanceRegisterQuery(new RegisterQuery(from, to, clinicId, IncludeVoided: false), RegisterExportFormat.Xlsx), ct);

    /// <summary>
    /// Aşı bilgi raporu (hayvan bazlı aşı uygulamaları, sayfalı). Süzgeçler: tarih aralığı, <c>productId</c>, <c>veterinarianId</c>
    /// (uygulamayı kaydeden kullanıcı). İptal edilenler <c>includeVoided=true</c> olmadıkça gelmez, sonradan eklenenler işaretlidir.
    /// Fiyat, ürünün güncel katalog fiyatıdır. Resmî biçime uygunluk doğrulanmadı.
    /// </summary>
    [HttpGet("registers/vaccine-info")]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<VaccineInfoRegisterItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetVaccineInfoRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] bool includeVoided = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
        => SendRegisterAsync(
            new GetVaccineInfoRegisterQuery(
                new RegisterQuery(from, to, clinicId, includeVoided, productId, veterinarianId), page, pageSize),
            ct);

    /// <summary>Aşı bilgi raporu CSV (UTF-8 BOM, <c>;</c>, Excel TR). Süzgeçler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("registers/vaccine-info/export")]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportVaccineInfoRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportVaccineInfoRegisterQuery(
                new RegisterQuery(from, to, clinicId, includeVoided, productId, veterinarianId), RegisterExportFormat.Csv),
            ct);

    /// <summary>Aşı bilgi raporu Excel (tarih/sayı hücre biçimi). Süzgeçler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("registers/vaccine-info/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Administrations.Read)]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [Authorize(Policy = PermissionCatalog.Products.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportVaccineInfoRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? productId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] bool includeVoided = false,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportVaccineInfoRegisterQuery(
                new RegisterQuery(from, to, clinicId, includeVoided, productId, veterinarianId), RegisterExportFormat.Xlsx),
            ct);

    /// <summary>
    /// HBS takibi — reçeteler (sayfalı; ADR-036 Dilim 2). <c>hbsStatus</c> verilmezse "Girilmedi" (iş listesi); <c>veterinarianId</c> geliş
    /// sorumlu hekimi. Bkz. docs/HBS_ASSISTANT_API_CONTRACT.md bölüm 6.
    /// </summary>
    [HttpGet("registers/hbs-prescriptions")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<HbsPrescriptionRegisterItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetHbsPrescriptionRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
        => SendRegisterAsync(
            new GetHbsPrescriptionRegisterQuery(HbsRegisterQuery(from, to, clinicId, veterinarianId, hbsStatus), page, pageSize), ct);

    /// <summary>HBS takibi — reçeteler CSV (UTF-8 BOM, <c>;</c>, Excel TR). Süzgeçler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("registers/hbs-prescriptions/export")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportHbsPrescriptionRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportHbsPrescriptionRegisterQuery(HbsRegisterQuery(from, to, clinicId, veterinarianId, hbsStatus), RegisterExportFormat.Csv), ct);

    /// <summary>HBS takibi — reçeteler Excel. Süzgeçler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("registers/hbs-prescriptions/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Prescriptions.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportHbsPrescriptionRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportHbsPrescriptionRegisterQuery(HbsRegisterQuery(from, to, clinicId, veterinarianId, hbsStatus), RegisterExportFormat.Xlsx), ct);

    /// <summary>
    /// HBS takibi — aşılar (sayfalı; ADR-036 Dilim 2): yalnız uygulanmış ve içe aktarılmamış aşılar. <c>hbsStatus</c> verilmezse "Girilmedi".
    /// </summary>
    [HttpGet("registers/hbs-vaccinations")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(RegisterReadResult<HbsVaccinationRegisterItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> GetHbsVaccinationRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
        => SendRegisterAsync(
            new GetHbsVaccinationRegisterQuery(HbsRegisterQuery(from, to, clinicId, veterinarianId, hbsStatus), page, pageSize), ct);

    /// <summary>HBS takibi — aşılar CSV (UTF-8 BOM, <c>;</c>, Excel TR). Süzgeçler JSON ile aynı; sayfa yok.</summary>
    [HttpGet("registers/hbs-vaccinations/export")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportHbsVaccinationRegister(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportHbsVaccinationRegisterQuery(HbsRegisterQuery(from, to, clinicId, veterinarianId, hbsStatus), RegisterExportFormat.Csv), ct);

    /// <summary>HBS takibi — aşılar Excel. Süzgeçler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("registers/hbs-vaccinations/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Vaccinations.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportHbsVaccinationRegisterXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? veterinarianId = null,
        [FromQuery] HbsEntryStatus? hbsStatus = null,
        CancellationToken ct = default)
        => SendRegisterExportAsync(
            new ExportHbsVaccinationRegisterQuery(HbsRegisterQuery(from, to, clinicId, veterinarianId, hbsStatus), RegisterExportFormat.Xlsx), ct);

    private static RegisterQuery HbsRegisterQuery(DateOnly from, DateOnly to, Guid? clinicId, Guid? veterinarianId, HbsEntryStatus? hbsStatus)
        => new(from, to, clinicId, IncludeVoided: false, VeterinarianUserId: veterinarianId, HbsStatus: hbsStatus);

    private async Task<IActionResult> SendRegisterAsync<T>(IRequest<Result<RegisterReadResult<T>>> request, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(request, ct);
        return result.ToActionResult(this);
    }

    /// <summary>CSV/XLSX dışa aktarma: dosya içerik türü, istekteki biçime göre seçilir.</summary>
    private async Task<IActionResult> SendRegisterExportAsync(IRequest<Result<RegisterExportResult>> request, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(request, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        var file = result.Value!;
        return File(
            file.Content,
            file.FileDownloadName.EndsWith(".xlsx", StringComparison.Ordinal) ? ReportFileContentTypes.Xlsx : ReportFileContentTypes.CsvUtf8,
            file.FileDownloadName);
    }
}
