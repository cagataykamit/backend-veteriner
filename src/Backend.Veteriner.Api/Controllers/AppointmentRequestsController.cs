using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.AppointmentRequests;
using Backend.Veteriner.Application.AppointmentRequests.Staff;
using Backend.Veteriner.Application.AppointmentRequests.Templates;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Notifications;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Online randevu talep kuyruğu (ADR-042 K4/K5/K7; APPT-010/011/016; docs/ONLINE_APPOINTMENT_API_CONTRACT.md §4).
/// <c>AppointmentRequests.Manage</c> gerekir. Atanmamış kliniğin talebi yokmuş gibi 404.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/appointment-requests")]
[Produces("application/json")]
[Authorize(Policy = PermissionCatalog.AppointmentRequests.Manage)]
public sealed class AppointmentRequestsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public AppointmentRequestsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Kuyruk ve takvim katmanı: <c>status</c>, <c>clinicId</c>, istenen saat aralığı <c>fromUtc</c>/<c>toUtc</c>.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AppointmentRequestListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? clinicId,
        [FromQuery] AppointmentRequestStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetAppointmentRequestsQuery(
            clinicId, status, fromUtc, toUtc, page ?? 1, pageSize ?? 20), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Kaynağa göre özet (TIER-016): <c>from</c>–<c>to</c> (İstanbul günleri, dahil; talep tarihi) kaynak başına talep ve onaylanan
    /// sayısı. Kapsam kuyrukla aynı.
    /// </summary>
    [HttpGet("channel-summary")]
    [ProducesResponseType(typeof(AppointmentRequestChannelSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChannelSummary(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? clinicId,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetAppointmentRequestChannelSummaryQuery(clinicId, from, to), ct)).ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AppointmentRequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetAppointmentRequestQuery(id), ct)).ToActionResult(this);
    }

    /// <summary>Gönderilecek e-postanın önizlemesi (onay: <c>Approved</c>/<c>Rescheduled</c>, red: <c>Rejected</c>).</summary>
    [HttpGet("{id:guid}/email-preview")]
    [ProducesResponseType(typeof(AppointmentRequestEmailPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PreviewEmail(
        [FromRoute] Guid id,
        [FromQuery] AppointmentRequestEmailKind kind,
        [FromQuery] DateTime? scheduledAtUtc,
        [FromQuery] string? rejectionReason,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new PreviewAppointmentRequestEmailQuery(id, kind, scheduledAtUtc, rejectionReason), ct))
            .ToActionResult(this);
    }

    /// <summary>Onay; farklı saat/hekim verilirse yeniden planlama. Randevu ve talep tek transaction'da.</summary>
    [HttpPost("{id:guid}/approve")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentRequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve([FromRoute] Guid id, [FromBody] ApproveAppointmentRequestBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new ApproveAppointmentRequestCommand(
            id, body.ClientId, body.PetId, body.ScheduledAtUtc, body.VeterinarianUserId, body.DurationMinutes, body.Email), ct))
            .ToActionResult(this);
    }

    /// <summary>Red; gerekçe zorunlu ve sahibe gider.</summary>
    [HttpPost("{id:guid}/reject")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentRequestDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject([FromRoute] Guid id, [FromBody] RejectAppointmentRequestBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new RejectAppointmentRequestCommand(id, body.Reason ?? string.Empty, body.Email), ct))
            .ToActionResult(this);
    }
}

/// <summary>
/// Klinik talep e-postası şablonları (ADR-042 K5; APPT-012): okuma <c>Clinics.Read</c>, yazma <c>Clinics.Update</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/appointment-request-email-templates")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicAppointmentRequestEmailTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicAppointmentRequestEmailTemplatesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(AppointmentRequestEmailTemplatesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetAppointmentRequestEmailTemplatesQuery(clinicId), ct)).ToActionResult(this);
    }

    [HttpPut("{kind}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentRequestEmailTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Put(
        [FromRoute] Guid clinicId,
        [FromRoute] AppointmentRequestEmailKind kind,
        [FromBody] EmailTemplateBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(
                new UpdateAppointmentRequestEmailTemplateCommand(clinicId, kind, body.Subject ?? string.Empty, body.Body ?? string.Empty), ct))
            .ToActionResult(this);
    }

    /// <summary>Kliniğin şablonunu siler; Vetinity varsayılanı döner.</summary>
    [HttpDelete("{kind}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(AppointmentRequestEmailTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reset([FromRoute] Guid clinicId, [FromRoute] AppointmentRequestEmailKind kind, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new ResetAppointmentRequestEmailTemplateCommand(clinicId, kind), ct)).ToActionResult(this);
    }

    [HttpPost("{kind}/preview")]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentRequestEmailPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(
        [FromRoute] Guid clinicId,
        [FromRoute] AppointmentRequestEmailKind kind,
        [FromBody] EmailTemplateBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(
                new PreviewAppointmentRequestEmailTemplateQuery(clinicId, kind, body.Subject ?? string.Empty, body.Body ?? string.Empty), ct))
            .ToActionResult(this);
    }
}

/// <summary>Oturumdaki kullanıcının bildirim merkezi (APPT-015, UX-008). Yalnız kendi bildirimleri.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notifications")]
[Produces("application/json")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public NotificationsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<UserNotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] UserNotificationType? type,
        [FromQuery] bool? unreadOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetMyNotificationsQuery(type, unreadOnly ?? false, page ?? 1, pageSize ?? 20), ct))
            .ToActionResult(this);
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UserNotificationUnreadCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetMyUnreadNotificationCountQuery(), ct)).ToActionResult(this);
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead([FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new MarkNotificationReadCommand(id), ct)).ToActionResult(this);
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead([FromQuery] UserNotificationType? type, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new MarkAllNotificationsReadCommand(type), ct)).ToActionResult(this);
    }
}

public sealed class ApproveAppointmentRequestBody
{
    public Guid? ClientId { get; init; }
    public Guid? PetId { get; init; }

    /// <summary>Boşsa istenen saat; farklıysa yeniden planlama.</summary>
    public DateTime? ScheduledAtUtc { get; init; }

    /// <summary>Boşsa o saatte boş uygun hekim atanır.</summary>
    public Guid? VeterinarianUserId { get; init; }

    public int? DurationMinutes { get; init; }

    /// <summary>Düzenlenmiş e-posta; boşsa şablon.</summary>
    public AppointmentRequestEmailOverride? Email { get; init; }
}

public sealed class RejectAppointmentRequestBody
{
    public string? Reason { get; init; }
    public AppointmentRequestEmailOverride? Email { get; init; }
}

public sealed class EmailTemplateBody
{
    public string? Subject { get; init; }
    public string? Body { get; init; }
}
