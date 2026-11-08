using Asp.Versioning;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Waitlist;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Bekleme listesi teklifinin kabul bağlantısı (kimliksiz; TIER-018; docs/WAITLIST_API_CONTRACT.md). Portalın <c>/bekleme-teklifi</c>
/// sayfası kullanır. TIER-019 deseni: token gövdede taşınır, sayfa bilgisi değişiklik yapmaz, kabul ve ret ayrı POST'tur; IP başına hız
/// sınırı (<c>portal-auth</c>); geçersiz, kullanılmış ya da süresi dolmuş token için sebep ayırt edilmeden 404
/// <c>WaitlistOfferLinks.NotFound</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}/waitlist-offer")]
[Produces("application/json")]
[Consumes("application/json")]
[AllowAnonymous]
[EnableRateLimiting("portal-auth")]
public sealed class PortalWaitlistOfferController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalWaitlistOfferController(IMediator mediator) => _mediator = mediator;

    /// <summary>Sayfa bilgisi: klinik adı, saat, hayvan adı, teklifin bitişi. Durumu değiştirmez.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(WaitlistOfferPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] WaitlistOfferLinkBody? body, CancellationToken ct)
        => (await _mediator.Send(new GetWaitlistOfferByLinkQuery(body?.Token ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>Kabul: randevu oluşur. Saat dolduysa 409 <c>WaitlistOffers.SlotTaken</c>.</summary>
    [HttpPost("accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept([FromBody] WaitlistOfferLinkBody? body, CancellationToken ct)
        => (await _mediator.Send(new AcceptWaitlistOfferByLinkCommand(body?.Token ?? string.Empty), ct)).ToActionResult(this);

    /// <summary>Ret: teklif kapanır, sıradakine geçilir; bekleme kaydı sırada kalır.</summary>
    [HttpPost("decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decline([FromBody] WaitlistOfferLinkBody? body, CancellationToken ct)
        => (await _mediator.Send(new DeclineWaitlistOfferByLinkCommand(body?.Token ?? string.Empty), ct)).ToActionResult(this);
}

public sealed class WaitlistOfferLinkBody
{
    public string? Token { get; init; }
}
