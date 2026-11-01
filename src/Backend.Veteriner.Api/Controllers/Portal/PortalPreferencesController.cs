using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Veteriner.Api.Common;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Application.OwnerPortal.DataRequests;
using Backend.Veteriner.Application.OwnerPortal.Preferences;
using Backend.Veteriner.Domain.OwnerPortal;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers.Portal;

/// <summary>
/// Hasta sahibi bildirim tercihleri ve KVKK veri talebi (PORTAL-007; docs/PORTAL_API_CONTRACT.md §10–11). Bağ kapsamlı uçlar sahipliği
/// ve klinik portal ayarını <c>OwnerPortalAccess</c> ile denetler.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/portal/v{version:apiVersion}")]
[Produces("application/json")]
[Authorize(Policy = OwnerPortalAuth.Policy)]
[EnableRateLimiting("portal")]
public sealed class PortalPreferencesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortalPreferencesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Hesap düzeyi tercih: push (sağlayıcı yapılandırılınca gönderilir).</summary>
    [HttpGet("me/notification-preferences")]
    [ProducesResponseType(typeof(PortalAccountNotificationPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAccountPreferences(CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPortalAccountNotificationPreferencesQuery(ownerAccountId), ct);
        return result.ToActionResult(this);
    }

    [HttpPut("me/notification-preferences")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalAccountNotificationPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateAccountPreferences([FromBody] PortalAccountPreferencesBody body, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new UpdatePortalAccountNotificationPreferencesCommand(ownerAccountId, body.PushEnabled), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bağ düzeyi tercih: hatırlatma (müşteri kaydı) ve ticari ileti onayı.</summary>
    [HttpGet("links/{linkId:guid}/notification-preferences")]
    [ProducesResponseType(typeof(PortalLinkNotificationPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLinkPreferences([FromRoute] Guid linkId, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPortalLinkNotificationPreferencesQuery(ownerAccountId, linkId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Kısmi güncelleme; KVKK "Ret" varken hatırlatma açma 409 <c>Clients.RemindersKvkkDeclined</c>.</summary>
    [HttpPut("links/{linkId:guid}/notification-preferences")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalLinkNotificationPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateLinkPreferences([FromRoute] Guid linkId, [FromBody] PortalLinkPreferencesBody body, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(
            new UpdatePortalLinkNotificationPreferencesCommand(ownerAccountId, linkId, body.RemindersOptedOut, body.MarketingConsented), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Bu klinikteki KVKK veri taleplerim (son 50).</summary>
    [HttpGet("links/{linkId:guid}/data-requests")]
    [ProducesResponseType(typeof(IReadOnlyList<PortalDataRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDataRequests([FromRoute] Guid linkId, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new GetPortalDataRequestsQuery(ownerAccountId, linkId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>KVKK başvurusu (erişim, düzeltme, silme, itiraz); klinik 30 gün içinde yanıtlar.</summary>
    [HttpPost("links/{linkId:guid}/data-requests")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PortalDataRequestDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDataRequest([FromRoute] Guid linkId, [FromBody] PortalDataRequestBody body, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new CreatePortalDataRequestCommand(ownerAccountId, linkId, body.Type, body.Message), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Henüz ele alınmamış talebi geri çeker.</summary>
    [HttpPost("links/{linkId:guid}/data-requests/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelDataRequest([FromRoute] Guid linkId, [FromRoute] Guid id, CancellationToken ct)
    {
        if (!this.TryGetOwnerAccountId(out var ownerAccountId, out var problem))
            return problem!;

        var result = await _mediator.Send(new CancelPortalDataRequestCommand(ownerAccountId, linkId, id), ct);
        return result.ToActionResult(this);
    }
}

public sealed record PortalAccountPreferencesBody(bool? PushEnabled);

public sealed record PortalLinkPreferencesBody(bool? RemindersOptedOut, bool? MarketingConsented);

public sealed record PortalDataRequestBody(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] OwnerDataRequestType? Type,
    string? Message);
