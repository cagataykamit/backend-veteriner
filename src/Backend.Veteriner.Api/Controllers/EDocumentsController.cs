using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.EDocuments.Commands.Actions;
using Backend.Veteriner.Application.EDocuments.Commands.Issue;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Application.EDocuments.Queries;
using Backend.Veteriner.Domain.EDocuments;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// e-belge (e-SMM / e-Arşiv / e-Fatura) kesme, izleme, iptal ve PDF (ADR-036 Dilim 3–4, E_DOCUMENTS_API_CONTRACT.md).
/// Belge kliniğin kendi entegratör hesabıyla kesilir; bağlantı klinik ayarlarındadır.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/e-documents")]
[Produces("application/json")]
[Authorize]
public sealed class EDocumentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public EDocumentsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Seçimin (ücretler / satış / ziyaret) belge grupları, alıcı ve eksik alanlar; sağlayıcıya belge gönderilmez.</summary>
    [HttpPost("preview")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Read)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EDocumentPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] EDocumentSourceBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new PreviewEDocumentQuery(body.ClinicId, body.ChargeIds, body.SaleId, body.VisitId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Tek belge keser. Aynı <c>requestId</c> ilk belgeyi döndürür. e-SMM genelde hemen kesilir; e-Arşiv / e-Fatura <c>Sending</c> döner.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.EDocuments.Issue)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Issue([FromBody] IssueEDocumentBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new IssueEDocumentCommand(body.RequestId, body.ClinicId, body.ChargeIds, body.SaleId, body.VisitId, body.Note), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Klinik kapsamlı belge listesi; oluşturma zamanı azalan, <c>toUtc</c> hariç.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.EDocuments.Read)]
    [ProducesResponseType(typeof(PagedResult<EDocumentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] EDocumentStatus? status = null,
        [FromQuery] EDocumentType? documentType = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetEDocumentsListQuery(clinicId, clientId, status, documentType, fromUtc, toUtc, page, pageSize), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Belgesi kesilmemiş, iptal edilmemiş ücretler (AK-4); oluşturma zamanı azalan.</summary>
    [HttpGet("undocumented-charges")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Read)]
    [ProducesResponseType(typeof(PagedResult<UndocumentedChargeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUndocumentedCharges(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? clinicId = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] Guid? visitId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new GetUndocumentedChargesQuery(clinicId, clientId, visitId, fromUtc, toUtc, page, pageSize), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Read)]
    [ProducesResponseType(typeof(EDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetEDocumentQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bekleyen belgenin sonucunu hemen sağlayıcıya sorar.</summary>
    [HttpPost("{id:guid}/refresh")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Read)]
    [ProducesResponseType(typeof(EDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Refresh([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new RefreshEDocumentCommand(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Hatalı belgeyi aynı dış referansla yeniden gönderir (çift belge oluşmaz).</summary>
    [HttpPost("{id:guid}/retry")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Issue)]
    [ProducesResponseType(typeof(EDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new RetryEDocumentCommand(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Hatalı belgeden vazgeçer; ücretler yeniden belgelenebilir. Gerekçe zorunlu.</summary>
    [HttpPost("{id:guid}/discard")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Issue)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Discard([FromRoute] Guid id, [FromBody] EDocumentReasonBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new DiscardEDocumentCommand(id, body.Reason), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kesilmiş belgeyi iptal süresi içinde iptal eder. Gerekçe zorunlu.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Cancel)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(EDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] Guid id, [FromBody] EDocumentReasonBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelEDocumentCommand(id, body.Reason), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Belge PDF'i; sağlayıcıdan alınır, saklanmaz.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Authorize(Policy = PermissionCatalog.EDocuments.Read)]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetPdf([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetEDocumentPdfQuery(id), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);
        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }
}

/// <summary>Belge kaynağı: tam olarak biri — <c>chargeIds</c>, <c>saleId</c> veya <c>visitId</c>.</summary>
public class EDocumentSourceBody
{
    public Guid? ClinicId { get; init; }
    public List<Guid>? ChargeIds { get; init; }
    public Guid? SaleId { get; init; }
    public Guid? VisitId { get; init; }
}

public sealed class IssueEDocumentBody : EDocumentSourceBody
{
    public Guid RequestId { get; init; }
    public string? Note { get; init; }
}

public sealed class EDocumentReasonBody
{
    public string? Reason { get; init; }
}
