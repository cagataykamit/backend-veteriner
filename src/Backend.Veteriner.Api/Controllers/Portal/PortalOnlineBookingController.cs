using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.AppointmentRequests;
using Backend.Veteriner.Application.AppointmentRequests.Portal;
using Backend.Veteriner.Application.AppointmentRequests.Public;
using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.OwnerPortal;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Kliniğe özel online randevu bağlantısı (kimliksiz; ADR-042 K1/K6; docs/ONLINE_APPOINTMENT_API_CONTRACT.md §5). Portal
/// uygulamasının <c>/randevu/{kod}</c> sayfası kullanır. IP başına hız sınırı (<c>portal-auth</c>); talep e-posta koduyla
/// doğrulanmadan oluşmaz. Kod geçersiz ya da online randevu kapalıysa sebep ayırt edilmeden 404 <c>OnlineBooking.NotFound</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}/booking/{bookingCode}")]
[Produces("application/json")]
[AllowAnonymous]
[EnableRateLimiting("portal-auth")]
public sealed class PortalOnlineBookingController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalOnlineBookingController(IMediator mediator) => _mediator = mediator;

    /// <summary>Sayfa: klinik iletişim bilgisi, talep sınırları ve online nedenler.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(OnlineBookingPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPage([FromRoute] string bookingCode, CancellationToken ct)
        => (await _mediator.Send(new GetOnlineBookingPageQuery(bookingCode), ct)).ToActionResult(this);

    /// <summary>Bir neden için müsait saatler (İstanbul günü <c>from</c>, en çok 14 gün).</summary>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(IReadOnlyList<OnlineAvailabilityDayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailability(
        [FromRoute] string bookingCode,
        [FromQuery] Guid reasonId,
        [FromQuery] DateOnly? from,
        [FromQuery] int? days,
        CancellationToken ct)
        => (await _mediator.Send(new GetOnlineBookingAvailabilityQuery(bookingCode, reasonId, from, days ?? 7), ct)).ToActionResult(this);

    /// <summary>E-posta doğrulama kodu ister; yanıt 204 (bekleme süresinde yeni kod gitmez).</summary>
    [HttpPost("verification-code")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestCode([FromRoute] string bookingCode, [FromBody] OnlineBookingCodeBody body, CancellationToken ct)
        => (await _mediator.Send(new RequestOnlineBookingCodeCommand(bookingCode, body.Email ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>Doğrulanmış e-postayla talep oluşturur; talep kesin randevu değildir.</summary>
    [HttpPost("requests")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentRequestCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRequest([FromRoute] string bookingCode, [FromBody] OnlineBookingRequestBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateOnlineBookingRequestCommand(
            bookingCode,
            body.Email ?? string.Empty,
            body.VerificationCode ?? string.Empty,
            body.ContactName ?? string.Empty,
            body.Phone ?? string.Empty,
            body.PetName ?? string.Empty,
            body.PetSpecies,
            body.PetBreed,
            body.ReasonId,
            body.RequestedStartUtc,
            body.Note), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }
}

/// <summary>
/// Oturumlu sahibin online talepleri (ADR-042 K1; docs/ONLINE_APPOINTMENT_API_CONTRACT.md §6): bağın kliniğinde talep sayfası,
/// müsaitlik, talep oluşturma, taleplerim ve iptal. Sahiplik <c>OwnerPortalAccess</c> ile denetlenir.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}/links/{linkId:guid}")]
[Produces("application/json")]
[Authorize(Policy = OwnerPortalAuth.Policy)]
[EnableRateLimiting("portal")]
public sealed class PortalAppointmentRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalAppointmentRequestsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("booking")]
    [ProducesResponseType(typeof(OnlineBookingPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBooking([FromRoute] Guid linkId, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;
        return (await _mediator.Send(new GetPortalBookingQuery(ownerAccountId, linkId), ct)).ToActionResult(this);
    }

    [HttpGet("booking/availability")]
    [ProducesResponseType(typeof(IReadOnlyList<OnlineAvailabilityDayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailability(
        [FromRoute] Guid linkId,
        [FromQuery] Guid reasonId,
        [FromQuery] DateOnly? from,
        [FromQuery] int? days,
        CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;
        return (await _mediator.Send(new GetPortalBookingAvailabilityQuery(ownerAccountId, linkId, reasonId, from, days ?? 7), ct))
            .ToActionResult(this);
    }

    [HttpGet("appointment-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<PortalAppointmentRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRequests([FromRoute] Guid linkId, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;
        return (await _mediator.Send(new GetPortalAppointmentRequestsQuery(ownerAccountId, linkId), ct)).ToActionResult(this);
    }

    [HttpPost("appointment-requests")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AppointmentRequestCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRequest([FromRoute] Guid linkId, [FromBody] PortalAppointmentRequestBody body, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreatePortalAppointmentRequestCommand(
            ownerAccountId,
            linkId,
            body.PetId,
            body.PetName,
            body.PetSpecies,
            body.PetBreed,
            body.ReasonId,
            body.RequestedStartUtc,
            body.Phone,
            body.Note), ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }

    [HttpPost("appointment-requests/{requestId:guid}/cancel")]
    [ProducesResponseType(typeof(PortalAppointmentRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromRoute] Guid linkId, [FromRoute] Guid requestId, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;
        return (await _mediator.Send(new CancelPortalAppointmentRequestCommand(ownerAccountId, linkId, requestId), ct)).ToActionResult(this);
    }
}

public sealed class OnlineBookingCodeBody
{
    public string? Email { get; init; }
}

public sealed class OnlineBookingRequestBody
{
    public string? Email { get; init; }
    public string? VerificationCode { get; init; }
    public string? ContactName { get; init; }
    public string? Phone { get; init; }
    public string? PetName { get; init; }
    public string? PetSpecies { get; init; }
    public string? PetBreed { get; init; }
    public Guid ReasonId { get; init; }
    public DateTime RequestedStartUtc { get; init; }
    public string? Note { get; init; }
}

public sealed class PortalAppointmentRequestBody
{
    /// <summary>Kayıtlı hayvan; boşsa <see cref="PetName"/> zorunlu.</summary>
    public Guid? PetId { get; init; }

    public string? PetName { get; init; }
    public string? PetSpecies { get; init; }
    public string? PetBreed { get; init; }
    public Guid ReasonId { get; init; }
    public DateTime RequestedStartUtc { get; init; }

    /// <summary>Boşsa müşteri kaydındaki telefon.</summary>
    public string? Phone { get; init; }

    public string? Note { get; init; }
}
