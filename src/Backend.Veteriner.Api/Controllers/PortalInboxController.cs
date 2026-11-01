using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Queries.GetWhatsAppLink;
using Backend.Veteriner.Application.Clinics.PortalSettings;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Application.OwnerPortal.DataRequests;
using Backend.Veteriner.Domain.OwnerPortal;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik tarafı portal yönetimi (PORTAL-009/010; docs/PORTAL_API_CONTRACT.md §11.2, §12, §13): portal gelen kutusu (KVKK veri
/// talepleri), klinik düzeyinde portal aç/kapat, bildirim tür × kanal matrisi ve müşteriye WhatsApp bağlantısı.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Produces("application/json")]
[Authorize]
public sealed class PortalInboxController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public PortalInboxController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Portal veri talepleri: açıklar önce (vade yakın önce), sonra en yeni.</summary>
    [HttpGet("portal-inbox/data-requests")]
    [Authorize(Policy = PermissionCatalog.OwnerPortal.Manage)]
    [ProducesResponseType(typeof(PagedResult<StaffDataRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDataRequests(
        [FromQuery] Guid? clinicId = null,
        [FromQuery] OwnerDataRequestStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetPortalInboxDataRequestsQuery(clinicId, status, page, pageSize), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Menü rozeti: açık ve süresi geçmiş veri talebi sayısı.</summary>
    [HttpGet("portal-inbox/summary")]
    [Authorize(Policy = PermissionCatalog.OwnerPortal.Manage)]
    [ProducesResponseType(typeof(PortalInboxSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary([FromQuery] Guid? clinicId = null, CancellationToken ct = default)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetPortalInboxSummaryQuery(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Talebi ele alır ya da kapatır; kapanışta yanıt notu zorunlu ve sahibe e-postayla gider.</summary>
    [HttpPost("portal-inbox/data-requests/{id:guid}/status")]
    [Authorize(Policy = PermissionCatalog.OwnerPortal.Manage)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(StaffDataRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus([FromRoute] Guid id, [FromBody] PortalDataRequestStatusBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new UpdatePortalDataRequestStatusCommand(id, body.Status, body.ResponseNote), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kliniğin portal ayarı; kayıt yoksa açık.</summary>
    [HttpGet("clinics/{clinicId:guid}/portal-settings")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicPortalSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPortalSettings([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicPortalSettingsQuery(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Portalı açar/kapatır; kapatınca hesap ve bağlar silinmez, portal erişimi durur.</summary>
    [HttpPut("clinics/{clinicId:guid}/portal-settings")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicPortalSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePortalSettings([FromRoute] Guid clinicId, [FromBody] ClinicPortalSettingsBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new UpdateClinicPortalSettingsCommand(clinicId, body.IsEnabled), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Hasta sahibi bildirim türü × kanal matrisi (salt okunur).</summary>
    [HttpGet("portal-notification-rules")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<OwnerNotificationRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotificationRules(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPortalNotificationRulesQuery(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Müşterinin WhatsApp sohbetini hazır metinle açan bağlantı; gönderimi personel yapar.</summary>
    [HttpGet("clients/{clientId:guid}/whatsapp-link")]
    [Authorize(Policy = PermissionCatalog.Clients.Read)]
    [ProducesResponseType(typeof(ClientWhatsAppLinkDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetWhatsAppLink(
        [FromRoute] Guid clientId,
        [FromQuery] WhatsAppLinkKind? kind,
        [FromQuery] Guid? targetId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClientWhatsAppLinkQuery(clientId, kind, targetId), ct);
        return result.ToActionResult(this);
    }
}

public sealed record PortalDataRequestStatusBody(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerDataRequestStatus? Status,
    string? ResponseNote);

public sealed record ClinicPortalSettingsBody(bool IsEnabled);
