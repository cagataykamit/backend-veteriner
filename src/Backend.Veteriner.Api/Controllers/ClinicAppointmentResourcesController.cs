using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Appointments.Resources;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik oda ve cihazları (ADR-053; APPT-025) — Ayarlar &gt; Tanımlar &gt; Oda ve cihazlar; bkz. docs/APPOINTMENT_RESOURCES_API_CONTRACT.md.
/// Okuma randevu planlama yetkisiyle (form ve takvim sütunları), yazma <c>Clinics.Update</c> ile.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/appointment-resources")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicAppointmentResourcesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicAppointmentResourcesController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Kliniğin oda ve cihazları (aktif/pasif dahil; odalar, cihazlar, sıra, ad).</summary>
    /// <remarks>Okuma yetkisi composite "any-of": <c>Clinics.Read</c>, <c>Clinics.Update</c>, <c>Appointments.Create</c>, <c>Appointments.Reschedule</c>.</remarks>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsSchedulingRead)]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentResourceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicAppointmentResourcesQuery(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Yeni oda ya da cihaz; aktifler arasında ad benzersizdir (409 <c>AppointmentResources.DuplicateName</c>).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentResourceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromRoute] Guid clinicId, [FromBody] AppointmentResourceBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new CreateAppointmentResourceCommand(clinicId, body.Name, body.Type, body.Capacity, body.SortOrder), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(List),
            new { version = HttpContext.RequestedApiVersion?.ToString() ?? "1.0", clinicId },
            result.Value);
    }

    /// <summary>Ad, kapasite ve sıra; tür değiştirilemez (400). Yanıt kaynağın güncel hali.</summary>
    [HttpPut("{resourceId:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentResourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid clinicId,
        [FromRoute] Guid resourceId,
        [FromBody] AppointmentResourceBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != resourceId)
            return Result.Failure("AppointmentResources.RouteIdMismatch", "Adresteki kayıt ile gönderilen kayıt eşleşmiyor. Sayfayı yenileyip tekrar deneyin.").ToActionResult(this);

        var result = await _mediator.Send(
            new UpdateAppointmentResourceCommand(clinicId, resourceId, body.Name, body.Type, body.Capacity, body.SortOrder), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kaynağı pasife alır (idempotent). Pasif kaynak yeni randevuda seçilemez; mevcut randevular etkilenmez.</summary>
    [HttpPost("{resourceId:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(AppointmentResourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Deactivate([FromRoute] Guid clinicId, [FromRoute] Guid resourceId, CancellationToken ct)
        => SetActive(clinicId, resourceId, isActive: false, ct);

    /// <summary>Kaynağı yeniden etkinleştirir (idempotent); ad çakışırsa 409 <c>AppointmentResources.DuplicateName</c>.</summary>
    [HttpPost("{resourceId:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(AppointmentResourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Activate([FromRoute] Guid clinicId, [FromRoute] Guid resourceId, CancellationToken ct)
        => SetActive(clinicId, resourceId, isActive: true, ct);

    private async Task<IActionResult> SetActive(Guid clinicId, Guid resourceId, bool isActive, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new SetAppointmentResourceActiveCommand(clinicId, resourceId, isActive), ct);
        return result.ToActionResult(this);
    }
}

/// <summary>POST/PUT /api/v1/clinics/{clinicId}/appointment-resources body; PUT'ta route id esastır.</summary>
public sealed class AppointmentResourceBody
{
    /// <summary>Optional; when set must match route resourceId.</summary>
    public Guid? Id { get; init; }

    public string Name { get; init; } = default!;

    /// <summary><c>Room</c> (oda) ya da <c>Equipment</c> (cihaz); sayı da kabul edilir (1, 2).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppointmentResourceType Type { get; init; }

    /// <summary>Aynı anda en çok kaç randevu (1–20).</summary>
    public int Capacity { get; init; } = 1;

    public int SortOrder { get; init; }
}
