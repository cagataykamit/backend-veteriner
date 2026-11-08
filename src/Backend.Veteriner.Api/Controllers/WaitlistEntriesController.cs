using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Waitlist;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Waitlist;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

public sealed record CreateWaitlistEntryBody(
    Guid ClinicId,
    Guid ClientId,
    Guid PetId,
    Guid? PreferredVeterinarianUserId,
    AppointmentType AppointmentType,
    Guid? ReasonId,
    int? DurationMinutes,
    DateOnly EarliestDate,
    DateOnly LatestDate,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistWeekdays PreferredDays,
    TimeOnly? PreferredFromTime,
    TimeOnly? PreferredToTime,
    string? Note);

public sealed record CloseWaitlistEntryBody(string? Reason);

public sealed record MoveWaitlistEntryBody([property: JsonConverter(typeof(JsonStringEnumConverter))] WaitlistMoveDirection Direction);

public sealed record SendWaitlistOfferBody(Guid SourceAppointmentId);

/// <summary>
/// Bekleme listesi, panel (TIER-018; docs/WAITLIST_API_CONTRACT.md). Liste ve kayıt geçmişi her pakette okunur; ekleme, sıra, elle
/// teklif ve boşalan saat bilgisi Profesyonel ve Kurumsal ister (403 <c>Subscriptions.FeatureNotInPlan</c>). Okuma randevu okuma,
/// yazma randevu oluşturma iznidir (yeni izin yok).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/waitlist-entries")]
[Produces("application/json")]
public sealed class WaitlistEntriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public WaitlistEntriesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<WaitlistEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] Guid? clinicId,
        [FromQuery] Guid? veterinarianUserId,
        [FromQuery] WaitlistEntryStatus? status,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetWaitlistEntriesQuery(clinicId, veterinarianUserId, status), ct)).ToActionResult(this);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(WaitlistEntryDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetWaitlistEntryQuery(id), ct)).ToActionResult(this);
    }

    /// <summary>İptal edilen randevunun boşalttığı saat ve ona uyan kayıtlar (iptal ekranı, elle teklif).</summary>
    [HttpGet("slots/{appointmentId:guid}")]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(WaitlistSlotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSlot(Guid appointmentId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetWaitlistSlotQuery(appointmentId), ct)).ToActionResult(this);
    }

    /// <summary>Bugün ekranı: sıradaki kayıt sayısı ve günün kalan saatlerinde boşalan saatler.</summary>
    [HttpGet("today")]
    [Authorize(Policy = PermissionCatalog.Appointments.Read)]
    [ProducesResponseType(typeof(WaitlistTodayDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetToday([FromQuery] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new GetWaitlistTodayQuery(clinicId), ct)).ToActionResult(this);
    }

    [HttpPost]
    [Consumes("application/json")]
    [Authorize(Policy = PermissionCatalog.Appointments.Create)]
    [ProducesResponseType(typeof(WaitlistEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateWaitlistEntryBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new CreateWaitlistEntryCommand(
            body.ClinicId, body.ClientId, body.PetId, body.PreferredVeterinarianUserId, body.AppointmentType, body.ReasonId,
            body.DurationMinutes, body.EarliestDate, body.LatestDate, body.PreferredDays, body.PreferredFromTime, body.PreferredToTime,
            body.Note), ct)).ToActionResult(this);
    }

    [HttpPost("{id:guid}/close")]
    [Consumes("application/json")]
    [Authorize(Policy = PermissionCatalog.Appointments.Create)]
    [ProducesResponseType(typeof(WaitlistEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(Guid id, [FromBody] CloseWaitlistEntryBody? body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new CloseWaitlistEntryCommand(id, body?.Reason), ct)).ToActionResult(this);
    }

    [HttpPost("{id:guid}/move")]
    [Consumes("application/json")]
    [Authorize(Policy = PermissionCatalog.Appointments.Create)]
    [ProducesResponseType(typeof(WaitlistEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveWaitlistEntryBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new MoveWaitlistEntryCommand(id, body.Direction), ct)).ToActionResult(this);
    }

    /// <summary>Elle teklif: boşalan saat seçilen kayda gönderilir (otomatik teklif kapalıyken de).</summary>
    [HttpPost("{id:guid}/offers")]
    [Consumes("application/json")]
    [Authorize(Policy = PermissionCatalog.Appointments.Create)]
    [ProducesResponseType(typeof(WaitlistOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendOffer(Guid id, [FromBody] SendWaitlistOfferBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        return (await _mediator.Send(new SendWaitlistOfferCommand(id, body.SourceAppointmentId), ct)).ToActionResult(this);
    }
}
