using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Application.OwnerPortal.Commands.AcceptPortalLegalDocuments;
using Backend.Veteriner.Application.OwnerPortal.Commands.AcknowledgePortalClinicDisclosure;
using Backend.Veteriner.Application.OwnerPortal.Commands.DeletePortalAccount;
using Backend.Veteriner.Application.OwnerPortal.Commands.RequestPortalAccountDeletionCode;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalClinicDisclosure;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalLegalStatus;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalMe;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Oturumlu hasta sahibi uçları (docs/PORTAL_API_CONTRACT.md §5.8–5.10): hesap, hesap silme, hukuki kabul, klinik aydınlatma metni.
/// Bağ kapsamlı uçlar sahipliği <c>OwnerPortalAccess</c> ile denetler.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}")]
[Produces("application/json")]
[Authorize(Policy = OwnerPortalAuth.Policy)]
[EnableRateLimiting("portal")]
public sealed class PortalAccountController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalAccountController(IMediator mediator) => _mediator = mediator;

    [HttpGet("me")]
    [ProducesResponseType(typeof(PortalMeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPortalMeQuery(ownerAccountId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Hesap silme onay kodunu hesabın e-postasına gönderir (bekleme süresindeyse yeni kod gitmez).</summary>
    [HttpPost("me/deletion-code")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RequestDeletionCode(CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new RequestPortalAccountDeletionCodeCommand(ownerAccountId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Hesabı kodla onaylayıp siler (ADR-041): anonimleştirme ve tüm klinik bağlarını kapatma; geri alınamaz.</summary>
    [HttpDelete("me")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteAccount([FromBody] PortalAccountDeletionBody body, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new DeletePortalAccountCommand(ownerAccountId, body.OneTimeCode ?? string.Empty), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bekleyen portal belgeleri ve onaylanmamış klinik aydınlatma metinleri.</summary>
    [HttpGet("me/legal-acceptances")]
    [ProducesResponseType(typeof(PortalLegalStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLegalStatus(CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPortalLegalStatusQuery(ownerAccountId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bekleyen portal belgelerinin güncel sürümünü kabul eder (ADR-029 kuralı).</summary>
    [HttpPost("me/legal-acceptances")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalLegalStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptLegalDocuments([FromBody] PortalLegalAcceptBody body, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new AcceptPortalLegalDocumentsCommand(ownerAccountId, body.Documents), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bağın kliniğinin aydınlatma metni (yer tutucular dolu).</summary>
    [HttpGet("links/{linkId:guid}/clinic-disclosure")]
    [ProducesResponseType(typeof(PortalClinicDisclosureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClinicDisclosure([FromRoute] Guid linkId, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPortalClinicDisclosureQuery(ownerAccountId, linkId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Aydınlatma metninin okunduğunu onaylar (ilk onay zamanı kalır).</summary>
    [HttpPost("links/{linkId:guid}/clinic-disclosure/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeClinicDisclosure([FromRoute] Guid linkId, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new AcknowledgePortalClinicDisclosureCommand(ownerAccountId, linkId), ct);
        return result.ToActionResult(this);
    }
}
