using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Auth.Commands.Logout;
using Backend.Veteriner.Application.Auth.Commands.LogoutAll;
using Backend.Veteriner.Application.Auth.Commands.Refresh;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    // POST /api/v1/auth/login
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginCommand? command, CancellationToken ct)
    {
        if (command is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "Ýstek gövdesi boþ veya hatalý JSON."
            );
        }

        Result<LoginResultDto> result = await _mediator.Send(command, ct);
        return result.ToActionResult(this);
    }

    // POST /api/v1/auth/refresh
    [AllowAnonymous]
    [EnableRateLimiting("refresh")]
    [HttpPost("refresh")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Refresh([FromBody] RefreshCommand? cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd?.RefreshToken))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed",
                detail: "refreshToken zorunludur."
            );
        }

        Result<LoginResultDto> result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    // POST /api/v1/auth/logout
    [Authorize]
    [HttpPost("logout")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand? cmd, CancellationToken ct)
    {
        // Idempotent no-op
        if (string.IsNullOrWhiteSpace(cmd?.RefreshToken))
            return Ok(new { ok = true, message = "Refresh token yok (no-op)." });

        // Kurumsal: kullanıcı kimliğini token'dan doğrula (handler'da da kontrol edilmeli)
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized();

        // ÖNERİLEN: LogoutCommand içinde UserId alanı varsa:
        // cmd = cmd with { UserId = userId };
        // (record değilse cmd.UserId = userId;)

        await _mediator.Send(cmd, ct);
        return Ok(new { ok = true });
    }

    // POST /api/v1/auth/logout-all
    [Authorize]
    [HttpPost("logout-all")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return Unauthorized();

        await _mediator.Send(new LogoutAllCommand(userId), ct);
        return Ok(new { ok = true });
    }
}
