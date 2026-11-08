using Asp.Versioning;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.OwnerPortal.MarketingOptOut;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Ticari iletideki ret bağlantısı (kimliksiz; ADR-046 K3; docs/AUTOMATED_REMINDERS_API_CONTRACT.md §2). Portalın
/// <c>/ticari-ileti-ret</c> sayfası kullanır. Token gövdede taşınır (TIER-019 deseni): istek yolu ve denetim kaydında görünmez. Sayfa
/// bilgisi durumu değiştirmez; ret ayrı POST'tur (e-posta tarayıcısı bağlantıyı açınca onay kalkmaz). Bağlantı tek kullanımlık değildir.
/// IP başına hız sınırı (<c>portal-auth</c>); geçersiz token 404 <c>MarketingOptOut.NotFound</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}/marketing-opt-out")]
[Produces("application/json")]
[Consumes("application/json")]
[AllowAnonymous]
[EnableRateLimiting("portal-auth")]
public sealed class PortalMarketingOptOutController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalMarketingOptOutController(IMediator mediator) => _mediator = mediator;

    /// <summary>Sayfa bilgisi: klinik adı ve onayın sürüp sürmediği. Durumu değiştirmez.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(MarketingOptOutPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] MarketingOptOutBody? body, CancellationToken ct)
        => (await _mediator.Send(new GetMarketingOptOutQuery(body?.Token ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>Ticari iletiyi reddeder: onay geri alınır, ret zamanı yazılır. Tekrar çağrı da 204'tür.</summary>
    [HttpPost("confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm([FromBody] MarketingOptOutBody? body, CancellationToken ct)
        => (await _mediator.Send(new ConfirmMarketingOptOutCommand(body?.Token ?? string.Empty), ct)).ToActionResult(this);
}

public sealed class MarketingOptOutBody
{
    public string? Token { get; init; }
}
