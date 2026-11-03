using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Kliniğin online randevu ayarları, bağlantı kodu ve müsaitlik (ADR-042 K1/K3/K6; docs/ONLINE_APPOINTMENT_API_CONTRACT.md §3).
/// Ayarlar mevcut klinik ayar yetkisiyle: okuma <c>Clinics.Read</c>, yazma <c>Clinics.Update</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/online-booking")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicOnlineBookingController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicOnlineBookingController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Ayarlar; kayıt yoksa varsayılan (kapalı, kod yok).</summary>
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.Clinics.Read)]
    [ProducesResponseType(typeof(ClinicOnlineBookingSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicOnlineBookingSettingsQuery(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Ayarları kaydeder; ilk kayıtta bağlantı kodu üretilir.</summary>
    [HttpPut]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ClinicOnlineBookingSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Put([FromRoute] Guid clinicId, [FromBody] OnlineBookingSettingsBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(
            new UpdateClinicOnlineBookingSettingsCommand(clinicId, body.IsEnabled, body.MinLeadMinutes, body.MaxAdvanceDays), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bağlantı kodunu yeniden üretir; eski bağlantı hemen kapanır.</summary>
    [HttpPost("regenerate-code")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(ClinicOnlineBookingSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegenerateCode([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new RegenerateClinicBookingCodeCommand(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Bir neden için müsait saatler ve boş uygun hekimler (personel; talep onayında farklı saat seçimi). <c>reasonId</c> boşsa
    /// "Diğer / emin değilim": 30 dk, tüm hekimler.
    /// </summary>
    /// <remarks>Okuma yetkisi composite "any-of": <c>Clinics.Read</c>, <c>Clinics.Update</c>, <c>Appointments.Create</c>, <c>Appointments.Reschedule</c>.</remarks>
    [HttpGet("availability")]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsSchedulingRead)]
    [ProducesResponseType(typeof(IReadOnlyList<StaffAvailabilityDayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailability(
        [FromRoute] Guid clinicId,
        [FromQuery] Guid? reasonId,
        [FromQuery] DateOnly? from,
        [FromQuery] int? days,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicAvailabilityQuery(clinicId, reasonId, from, days ?? 7), ct);
        return result.ToActionResult(this);
    }
}

/// <summary>PUT /api/v1/clinics/{clinicId}/online-booking body.</summary>
public sealed class OnlineBookingSettingsBody
{
    public bool IsEnabled { get; init; }
    public int MinLeadMinutes { get; init; }
    public int MaxAdvanceDays { get; init; }
}
