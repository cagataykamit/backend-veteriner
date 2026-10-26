using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.LegalAcceptances.Queries.GetCurrentVersions;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Application.OwnerPortal.Commands.AcceptPortalInvite;
using Backend.Veteriner.Application.OwnerPortal.Commands.ConfirmPortalPasswordReset;
using Backend.Veteriner.Application.OwnerPortal.Commands.PortalCodeSignIn;
using Backend.Veteriner.Application.OwnerPortal.Commands.PortalLogout;
using Backend.Veteriner.Application.OwnerPortal.Commands.PortalPasswordSignIn;
using Backend.Veteriner.Application.OwnerPortal.Commands.RefreshPortalSession;
using Backend.Veteriner.Application.OwnerPortal.Commands.RequestPortalCode;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalInvitePreview;
using Backend.Veteriner.Domain.OwnerPortal;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Hasta sahibi portalı kimlik uçları (ADR-031 K3/K6; docs/PORTAL_API_CONTRACT.md §5). Ayrı yüzey <c>/api/portal</c>, ayrı şema;
/// personel token'ı burada geçmez. Kod ve sıfırlama isteği hesap var/yok her durumda 204; giriş hataları tek tip 401.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}")]
[Produces("application/json")]
public sealed class PortalAuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalAuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>Portal hukuki belgelerinin güncel sürümleri (hasta sahibi varyantı).</summary>
    [AllowAnonymous]
    [HttpGet("legal-documents")]
    [ProducesResponseType(typeof(IReadOnlyList<LegalDocumentVersionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLegalDocuments(CancellationToken ct)
        => Ok(await _mediator.Send(new GetCurrentLegalDocumentVersionsQuery(OwnerPortal: true), ct));

    /// <summary>Davet önizlemesi. Geçersiz/süresi dolmuş davet 404 <c>Portal.Invites.NotFound</c> (sebep ayırt edilmez).</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-auth")]
    [HttpGet("invites/{token}")]
    [ProducesResponseType(typeof(PortalInvitePreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvite([FromRoute] string token, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPortalInvitePreviewQuery(token), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Daveti kabul eder; hesap yoksa oluşturur, bağı kurar ve oturum açar.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-auth")]
    [HttpPost("invites/{token}/accept")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalInviteAcceptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptInvite([FromRoute] string token, [FromBody] PortalInviteAcceptBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new AcceptPortalInviteCommand(token, body.DisplayName, body.Password, body.AcceptedLegalDocuments), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Giriş kodu ister; yanıt her durumda 204.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-auth")]
    [HttpPost("auth/code")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestSignInCode([FromBody] PortalEmailBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RequestPortalCodeCommand(body.Email ?? string.Empty, OwnerPortalCodePurpose.SignIn), ct);
        return result.ToActionResult(this);
    }

    /// <summary>E-posta + kodla giriş.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-auth")]
    [HttpPost("auth/code/verify")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalAuthTokensDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifySignInCode([FromBody] PortalCodeSignInBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new PortalCodeSignInCommand(body.Email ?? string.Empty, body.OneTimeCode ?? string.Empty), ct);
        return result.ToActionResult(this);
    }

    /// <summary>E-posta + parolayla giriş.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-auth")]
    [HttpPost("auth/login")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalAuthTokensDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PasswordSignIn([FromBody] PortalPasswordSignInBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new PortalPasswordSignInCommand(body.Email ?? string.Empty, body.Password ?? string.Empty), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Parola sıfırlama kodu ister; yanıt her durumda 204.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-auth")]
    [HttpPost("auth/password-reset")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordReset([FromBody] PortalEmailBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RequestPortalCodeCommand(body.Email ?? string.Empty, OwnerPortalCodePurpose.PasswordReset), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kodla yeni parola belirler; tüm portal oturumları kapanır.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-auth")]
    [HttpPost("auth/password-reset/confirm")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmPasswordReset([FromBody] PortalPasswordResetConfirmBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new ConfirmPortalPasswordResetCommand(
            body.Email ?? string.Empty,
            body.OneTimeCode ?? string.Empty,
            body.NewPassword ?? string.Empty), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Portal oturumunu yeniler (jeton döndürülür).</summary>
    [AllowAnonymous]
    [EnableRateLimiting("portal-refresh")]
    [HttpPost("auth/refresh")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalAuthTokensDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] PortalRefreshBody body, CancellationToken ct)
    {
        var result = await _mediator.Send(new RefreshPortalSessionCommand(body.RefreshToken ?? string.Empty), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bu oturumdan çıkış (refresh iptal).</summary>
    [Authorize(Policy = OwnerPortalAuth.Policy)]
    [EnableRateLimiting("portal")]
    [HttpPost("auth/logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PortalRefreshBody? body, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new PortalLogoutCommand(ownerAccountId, body?.RefreshToken, AllSessions: false), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Tüm cihazlardan çıkış.</summary>
    [Authorize(Policy = OwnerPortalAuth.Policy)]
    [EnableRateLimiting("portal")]
    [HttpPost("auth/logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new PortalLogoutCommand(ownerAccountId, null, AllSessions: true), ct);
        return result.ToActionResult(this);
    }
}
