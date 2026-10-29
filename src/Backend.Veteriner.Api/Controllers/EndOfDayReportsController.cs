using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Payments;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Gün sonu raporları (D6): günlük işlem dökümü, tahsilat/kasa özeti, gün sonu açık işler.
/// Sözleşme: <c>docs/DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md</c>. <c>from</c>/<c>to</c>: İstanbul takvim günleri, ikisi dahil (en çok 366 gün).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports")]
[Produces("application/json")]
[Authorize]
public sealed class EndOfDayReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public EndOfDayReportsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Günlük işlem dökümü (sayfalı, zaman sıralı): ücret, tahsilat, iptal ve düzeltme olayları + süzgece uyan tüm olayların toplamları.</summary>
    [HttpGet("daily-transactions")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(DailyTransactionsReportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDailyTransactions(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] DailyTransactionKind? kind = null,
        [FromQuery] PaymentMethod? paymentMethod = null,
        [FromQuery] Guid? performedByUserId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = RegisterReportConstants.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetDailyTransactionsReportQuery(from, to, clinicId, kind, paymentMethod, performedByUserId, page, pageSize), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Günlük işlem dökümü CSV (UTF-8 BOM, <c>;</c>, Excel TR). Süzgeç ve sıralama JSON ile aynı; sayfa yok.</summary>
    [HttpGet("daily-transactions/export")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportDailyTransactions(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] DailyTransactionKind? kind = null,
        [FromQuery] PaymentMethod? paymentMethod = null,
        [FromQuery] Guid? performedByUserId = null,
        CancellationToken ct = default)
        => SendExportAsync(
            new ExportDailyTransactionsReportQuery(from, to, clinicId, kind, paymentMethod, performedByUserId, RegisterExportFormat.Csv),
            ReportFileContentTypes.CsvUtf8, ct);

    /// <summary>Günlük işlem dökümü Excel (tarih-saat/tutar hücre biçimi). Süzgeçler CSV ile aynı; sayfa yok.</summary>
    [HttpGet("daily-transactions/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportDailyTransactionsXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] DailyTransactionKind? kind = null,
        [FromQuery] PaymentMethod? paymentMethod = null,
        [FromQuery] Guid? performedByUserId = null,
        CancellationToken ct = default)
        => SendExportAsync(
            new ExportDailyTransactionsReportQuery(from, to, clinicId, kind, paymentMethod, performedByUserId, RegisterExportFormat.Xlsx),
            ReportFileContentTypes.Xlsx, ct);

    /// <summary>Günlük işlem dökümü PDF (A4 yatay, yazdırmaya uygun; REPORT-007). Filtreler CSV ile aynı; sayfa yok. Satır tavanı: deneme 1.000, ücretli 5.000.</summary>
    [HttpGet("daily-transactions/export-pdf")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [Authorize(Policy = PermissionCatalog.Charges.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportDailyTransactionsPdf(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] DailyTransactionKind? kind = null,
        [FromQuery] PaymentMethod? paymentMethod = null,
        [FromQuery] Guid? performedByUserId = null,
        CancellationToken ct = default)
        => SendExportAsync(
            new ExportDailyTransactionsReportQuery(from, to, clinicId, kind, paymentMethod, performedByUserId, RegisterExportFormat.Pdf),
            ReportFileContentTypes.Pdf, ct);

    /// <summary>Tahsilat/kasa özeti: ödeme türüne göre toplam, iptal edilen tahsilatlar, günlük ve kullanıcı kırılımı.</summary>
    [HttpGet("cash-summary")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(CashSummaryReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCashSummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetCashSummaryReportQuery(from, to, clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kasa özeti CSV (düz tablo: Toplam, Ödeme Türü, Gün, Kullanıcı bölümleri).</summary>
    [HttpGet("cash-summary/export")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportCashSummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
        => SendExportAsync(new ExportCashSummaryReportQuery(from, to, clinicId, RegisterExportFormat.Csv), ReportFileContentTypes.CsvUtf8, ct);

    /// <summary>Kasa özeti Excel. Süzgeçler CSV ile aynı.</summary>
    [HttpGet("cash-summary/export-xlsx")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportCashSummaryXlsx(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
        => SendExportAsync(new ExportCashSummaryReportQuery(from, to, clinicId, RegisterExportFormat.Xlsx), ReportFileContentTypes.Xlsx, ct);

    /// <summary>Kasa özeti PDF (A4 yatay, yazdırmaya uygun; REPORT-007). Filtreler CSV ile aynı; özet satırları olduğu için satır tavanı yok.</summary>
    [HttpGet("cash-summary/export-pdf")]
    [Authorize(Policy = PermissionCatalog.Payments.Read)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ExportCashSummaryPdf(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
        => SendExportAsync(new ExportCashSummaryReportQuery(from, to, clinicId, RegisterExportFormat.Pdf), ReportFileContentTypes.Pdf, ct);

    /// <summary>Gün sonu açık işler (tek klinik): ücretsiz ziyaret, ödeme bekleyen, bekleyen lab, tamamlanmamış geliş. Hesap <c>IVisitOpenItemsReader</c>'dadır.</summary>
    [HttpGet("end-of-day/open-items")]
    [Authorize(Policy = PermissionCatalog.Visits.Read)]
    [ProducesResponseType(typeof(EndOfDayOpenItemsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEndOfDayOpenItems(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetEndOfDayOpenItemsQuery(from, to, clinicId), ct);
        return result.ToActionResult(this);
    }

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
