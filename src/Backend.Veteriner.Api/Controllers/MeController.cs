using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth.Commands.ChangePassword;
using Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeAllMy;
using Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeMy;
using Backend.Veteriner.Application.Auth.Queries.Me;
using Backend.Veteriner.Application.Auth.Queries.Sessions;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Queries.GetMyClinics;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/me")]
[Authorize]
[Produces("application/json")]
public sealed class MeController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Get all sessions for the current user.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<SessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        var result = await _mediator.Send(new ListSessionsQuery(), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Oturum açmış kullanıcının hesap özetini döner (read-only).
    /// </summary>
    [HttpGet("account-summary")]
    [ProducesResponseType(typeof(AccountSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountSummary(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAccountSummaryQuery(), ct);
        return result.ToActionResult(this);
    }

    [HttpGet("clinics")]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyClinics([FromQuery] bool? isActive, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetMyClinicsQuery(isActive), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Revoke a single session by id. Only the owner can revoke.
    /// </summary>
    [HttpDelete("sessions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RevokeMySessionCommand(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Oturum açmış kullanıcının mevcut şifresini doğrulayarak yeni şifre belirler.
    /// </summary>
    [HttpPost("change-password")]
    [EnableRateLimiting("change-password")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand? command, CancellationToken ct)
    {
        if (command is null)
        {
            return Result.Failure(
                    "Auth.Validation.InvalidRequestBody",
                    "İstek gövdesi boş veya hatalı JSON.")
                .ToActionResult(this);
        }

        var result = await _mediator.Send(command, ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);

        return Ok(new { ok = true });
    }

    /// <summary>
    /// Revoke all sessions for the current user.
    /// </summary>
    [HttpDelete("sessions/all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await _mediator.Send(new RevokeAllMySessionsCommand(userId.Value), ct);
        return result.ToActionResult(this);
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
