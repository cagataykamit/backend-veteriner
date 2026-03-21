using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeAllMy;
using Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeMy;
using Backend.Veteriner.Application.Auth.Queries.Sessions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
