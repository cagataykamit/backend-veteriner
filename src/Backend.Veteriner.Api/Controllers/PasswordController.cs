using Backend.Veteriner.Application.Auth.PasswordReset.Commands.Request;
using Backend.Veteriner.Application.Auth.PasswordReset.Commands.Confirm;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class PasswordController : ControllerBase
{
    private readonly IMediator _mediator;
    public PasswordController(IMediator mediator) => _mediator = mediator;

    // POST /api/v1/password/request-reset
    [AllowAnonymous]
    [EnableRateLimiting("password-reset-request")]
    [HttpPost("request-reset")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> RequestReset([FromBody] RequestPasswordResetCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return Ok(new { ok = true });
    }

    // POST /api/v1/password/confirm
    [AllowAnonymous]
    [EnableRateLimiting("password-reset-confirm")]
    [HttpPost("confirm")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmPasswordResetCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);
        return Ok(new { ok = true });
    }
}
