using Asp.Versioning;
using Backend.Veteriner.Api.Auth;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Middleware;
using Backend.Veteriner.Application.ExternalApi;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.External;

/// <summary>
/// Dış API v1, salt okunur (TIER-009; docs/EXTERNAL_API_CONTRACT.md). Kimlik yalnız <c>X-Api-Key</c>; kiracı anahtardan çözülür ve
/// ADR-040 kiracı filtresi bu kiracıyla çalışır. Uç başına bir kapsam; anahtar başına hız sınırı; yalnız Kurumsal paket. Yazma ucu yoktur.
/// Sayfalama imleçlidir: sıra (<c>changedAtUtc</c>, <c>id</c>), <c>nextCursor</c> null ise son sayfa.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/external/v{version:apiVersion}")]
[Produces("application/json")]
[Authorize(Policy = ExternalApiAuth.Policy)]
[EnableRateLimiting(RateLimitingExtensions.ExternalApiPolicy)]
[ExternalApiAccess]
public sealed class ExternalApiController : ControllerBase
{
    private readonly IMediator _mediator;

    public ExternalApiController(IMediator mediator) => _mediator = mediator;

    /// <summary>Müşteriler (peşin satış müşterisi hariç): ad, telefon, e-posta. Kimlik/vergi numarası ve adres verilmez.</summary>
    [HttpGet("clients")]
    [Authorize(Policy = ExternalApiAuth.Policies.ClientsRead)]
    [ProducesResponseType(typeof(ExternalPage<ExternalClientDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetClients(
        [FromQuery] DateTime? updatedSince, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct)
        => (await _mediator.Send(new GetExternalClientsQuery(new ExternalPageRequest(updatedSince, cursor, pageSize)), ct)).ToActionResult(this);

    /// <summary>Hayvanlar: ad, tür, ırk, cinsiyet, doğum tarihi, kısırlaştırma ve yaşam durumu. Not, uyarı ve kimlik numaraları verilmez.</summary>
    [HttpGet("pets")]
    [Authorize(Policy = ExternalApiAuth.Policies.PetsRead)]
    [ProducesResponseType(typeof(ExternalPage<ExternalPetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetPets(
        [FromQuery] DateTime? updatedSince, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct)
        => (await _mediator.Send(new GetExternalPetsQuery(new ExternalPageRequest(updatedSince, cursor, pageSize)), ct)).ToActionResult(this);

    /// <summary>Randevular: zaman, süre, tür ve durum. Not, randevu nedeni ve hekim verilmez.</summary>
    [HttpGet("appointments")]
    [Authorize(Policy = ExternalApiAuth.Policies.AppointmentsRead)]
    [ProducesResponseType(typeof(ExternalPage<ExternalAppointmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] DateTime? updatedSince, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct)
        => (await _mediator.Send(new GetExternalAppointmentsQuery(new ExternalPageRequest(updatedSince, cursor, pageSize)), ct)).ToActionResult(this);

    /// <summary>Aşılar: aşı adı, durum, uygulama ve plan tarihi. Not, uygulama yolu ve muayene bağlantısı verilmez.</summary>
    [HttpGet("vaccinations")]
    [Authorize(Policy = ExternalApiAuth.Policies.VaccinationsRead)]
    [ProducesResponseType(typeof(ExternalPage<ExternalVaccinationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetVaccinations(
        [FromQuery] DateTime? updatedSince, [FromQuery] string? cursor, [FromQuery] int? pageSize, CancellationToken ct)
        => (await _mediator.Send(new GetExternalVaccinationsQuery(new ExternalPageRequest(updatedSince, cursor, pageSize)), ct)).ToActionResult(this);
}
