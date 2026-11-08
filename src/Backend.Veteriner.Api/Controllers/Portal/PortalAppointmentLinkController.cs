using Asp.Versioning;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Randevu hatırlatmasındaki "onayla / iptal et" bağlantısı (kimliksiz; TIER-019; docs/APPOINTMENT_OWNER_LINK_API_CONTRACT.md).
/// Portalın <c>/randevu-yanit</c> sayfası kullanır. Token gövdede taşınır: istek yolu ve denetim kaydında görünmez. Sayfa
/// bilgisi değişiklik yapmaz; eylemler ayrı POST'tur (e-posta tarayıcısı bağlantıyı açınca randevu değişmez). IP başına hız
/// sınırı (<c>portal-auth</c>); geçersiz, kullanılmış ya da süresi dolmuş token için sebep ayırt edilmeden 404
/// <c>AppointmentLinks.NotFound</c>. "Takvime ekle" (TIER-020) .ics uçları aynı token desenini kullanır.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}/appointment-link")]
[Produces("application/json")]
[Consumes("application/json")]
[AllowAnonymous]
[EnableRateLimiting("portal-auth")]
public sealed class PortalAppointmentLinkController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalAppointmentLinkController(IMediator mediator) => _mediator = mediator;

    /// <summary>Sayfa bilgisi: klinik adı, randevu saati, hayvan adı, iptal açık mı. Durumu değiştirmez.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(AppointmentOwnerLinkPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] AppointmentOwnerLinkBody? body, CancellationToken ct)
        => (await _mediator.Send(new GetAppointmentOwnerLinkQuery(body?.Token ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>Sahip geleceğini onaylar; randevu durumu değişmez. Bağlantı kapanır.</summary>
    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm([FromBody] AppointmentOwnerLinkBody? body, CancellationToken ct)
        => (await _mediator.Send(new ConfirmAppointmentByOwnerLinkCommand(body?.Token ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>Sahip randevuyu iptal eder. Klinik bağlantıdan iptali kapattıysa 409 <c>AppointmentLinks.CancelDisabled</c>.</summary>
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel([FromBody] AppointmentOwnerLinkBody? body, CancellationToken ct)
        => (await _mediator.Send(new CancelAppointmentByOwnerLinkCommand(body?.Token ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>
    /// E-postadaki "takvime ekle" bağlantısı (TIER-020): takvim tokeniyle .ics indirir. GET güvenlidir: yalnız dosya döner,
    /// hiçbir şeyi değiştirmez. Takvim tokeni eylem yapamaz.
    /// </summary>
    [HttpGet("calendar.ics")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> CalendarFile([FromQuery] string? token, CancellationToken ct)
        => SendCalendarAsync(token, ct);

    /// <summary><c>/randevu-yanit</c> sayfasındaki ".ics indir" düğmesi: token gövdede; onaydan sonra da çalışır.</summary>
    [HttpPost("calendar")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Calendar([FromBody] AppointmentOwnerLinkBody? body, CancellationToken ct)
        => SendCalendarAsync(body?.Token, ct);

    private async Task<IActionResult> SendCalendarAsync(string? token, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAppointmentCalendarByOwnerLinkQuery(token ?? string.Empty), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        Response.Headers.CacheControl = "no-store";
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
}

public sealed class AppointmentOwnerLinkBody
{
    public string? Token { get; init; }
}
