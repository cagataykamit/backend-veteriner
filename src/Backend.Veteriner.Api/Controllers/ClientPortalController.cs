using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Commands.CloseClientPortalLink;
using Backend.Veteriner.Application.OwnerPortal.Commands.CreateClientPortalInvite;
using Backend.Veteriner.Application.OwnerPortal.Commands.RevokeClientPortalInvite;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetClientPortalAccess;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>
/// Klinik paneli: müşterinin hasta sahibi portalı daveti ve erişimi (ADR-031; docs/PORTAL_API_CONTRACT.md §4).
/// Yalnız <c>OwnerPortal.Manage</c> (Admin, Owner, ClinicAdmin). Kiracı JWT <c>tenant_id</c>, klinik context-first.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/clients/{clientId:guid}")]
[Produces("application/json")]
[Authorize(Policy = PermissionCatalog.OwnerPortal.Manage)]
public sealed class ClientPortalController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public ClientPortalController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    /// <summary>Müşteri kartı portal bölümü: bağlar, son 20 davet ve davet edilebilirlik.</summary>
    [HttpGet("portal-access")]
    [ProducesResponseType(typeof(ClientPortalAccessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccess([FromRoute] Guid clientId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetClientPortalAccessQuery(clientId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Portal daveti gönderir (müşteri kaydındaki e-postaya). KVKK Ret / hatırlatma istemiyor müşteriye 409
    /// <c>OwnerPortal.ClientOptedOut</c>; e-posta yoksa 409 <c>OwnerPortal.ClientEmailMissing</c>. Ham token yanıtta dönmez.
    /// </summary>
    [HttpPost("portal-invites")]
    [ProducesResponseType(typeof(ClientPortalInviteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvite(
        [FromRoute] Guid clientId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateClientPortalInviteBody? body,
        CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreateClientPortalInviteCommand(clientId, body?.ClinicId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bekleyen daveti iptal eder.</summary>
    [HttpPost("portal-invites/{inviteId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevokeInvite([FromRoute] Guid clientId, [FromRoute] Guid inviteId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new RevokeClientPortalInviteCommand(clientId, inviteId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Hasta sahibinin bu müşteri kaydına portal erişimini kapatır (bağ silinmez).</summary>
    [HttpPost("portal-links/{linkId:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CloseLink([FromRoute] Guid clientId, [FromRoute] Guid linkId, CancellationToken ct)
    {
        if (!this.TryGetResolvedTenant(_tenantContext, out _, out var problem))
            return problem!;

        var result = await _mediator.Send(new CloseClientPortalLinkCommand(clientId, linkId), ct);
        return result.ToActionResult(this);
    }
}

/// <summary>POST /api/v1/clients/{clientId}/portal-invites gövdesi (opsiyonel).</summary>
public sealed class CreateClientPortalInviteBody
{
    /// <summary>Daveti gönderen klinik; aktif klinik bağlamı varsa yalnız uyuşmazlık denetimi için.</summary>
    public Guid? ClinicId { get; init; }
}
