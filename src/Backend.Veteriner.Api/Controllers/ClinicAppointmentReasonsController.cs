using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Appointments.Reasons;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik randevu nedenleri (ADR-042 K2; APPT-005/006/007) — Ayarlar &gt; Randevu; bkz. docs/ONLINE_APPOINTMENT_API_CONTRACT.md §2.
/// Okuma randevu planlama yetkisiyle (form süre önerisi), yazma <c>Clinics.Update</c> ile.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clinics/{clinicId:guid}/appointment-reasons")]
[Produces("application/json")]
[Authorize]
public sealed class ClinicAppointmentReasonsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClinicAppointmentReasonsController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Kliniğin nedenleri (aktif/pasif dahil; sıra, sonra ad).</summary>
    /// <remarks>Okuma yetkisi composite "any-of": <c>Clinics.Read</c>, <c>Clinics.Update</c>, <c>Appointments.Create</c>, <c>Appointments.Reschedule</c>.</remarks>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicyNames.ClinicsSchedulingRead)]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentReasonDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List([FromRoute] Guid clinicId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new GetClinicAppointmentReasonsQuery(clinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Yeni neden; aktif nedenler arasında ad benzersizdir (409 <c>AppointmentReasons.DuplicateName</c>).</summary>
    [HttpPost]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentReasonDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromRoute] Guid clinicId, [FromBody] AppointmentReasonBody body, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreateAppointmentReasonCommand(
            clinicId,
            body.Name,
            body.DefaultDurationMinutes,
            body.IsOnlineBookable,
            body.SortOrder,
            body.VeterinarianUserIds), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return CreatedAtAction(
            nameof(List),
            new { version = HttpContext.RequestedApiVersion?.ToString() ?? "1.0", clinicId },
            result.Value);
    }

    /// <summary>Neden alanları; yanıt nedenin güncel hali.</summary>
    [HttpPut("{reasonId:guid}")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentReasonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid clinicId,
        [FromRoute] Guid reasonId,
        [FromBody] AppointmentReasonBody body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        if (body.Id is { } bid && bid != Guid.Empty && bid != reasonId)
            return Result.Failure("AppointmentReasons.RouteIdMismatch", "Route id ile body id uyusmuyor.").ToActionResult(this);

        var result = await _mediator.Send(new UpdateAppointmentReasonCommand(
            clinicId,
            reasonId,
            body.Name,
            body.DefaultDurationMinutes,
            body.IsOnlineBookable,
            body.SortOrder,
            body.VeterinarianUserIds), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Nedeni pasife alır (idempotent). Pasif neden yeni randevu ve online talepte seçilemez.</summary>
    [HttpPost("{reasonId:guid}/deactivate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(AppointmentReasonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Deactivate([FromRoute] Guid clinicId, [FromRoute] Guid reasonId, CancellationToken ct)
        => SetActive(clinicId, reasonId, isActive: false, ct);

    /// <summary>Nedeni yeniden etkinleştirir (idempotent); ad çakışırsa 409 <c>AppointmentReasons.DuplicateName</c>.</summary>
    [HttpPost("{reasonId:guid}/activate")]
    [Authorize(Policy = PermissionCatalog.Clinics.Update)]
    [ProducesResponseType(typeof(AppointmentReasonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public Task<IActionResult> Activate([FromRoute] Guid clinicId, [FromRoute] Guid reasonId, CancellationToken ct)
        => SetActive(clinicId, reasonId, isActive: true, ct);

    private async Task<IActionResult> SetActive(Guid clinicId, Guid reasonId, bool isActive, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;
        var result = await _mediator.Send(new SetAppointmentReasonActiveCommand(clinicId, reasonId, isActive), ct);
        return result.ToActionResult(this);
    }
}

/// <summary>POST/PUT /api/v1/clinics/{clinicId}/appointment-reasons body; PUT'ta route id esastır.</summary>
public sealed class AppointmentReasonBody
{
    /// <summary>Optional; when set must match route reasonId.</summary>
    public Guid? Id { get; init; }

    public string Name { get; init; } = default!;
    public int DefaultDurationMinutes { get; init; }
    public bool IsOnlineBookable { get; init; }
    public int SortOrder { get; init; }

    /// <summary>Boş/null = klinikteki tüm hekimler uygun.</summary>
    public IReadOnlyList<Guid>? VeterinarianUserIds { get; init; }
}
