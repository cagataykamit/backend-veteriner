using Backend.Veteriner.Application.EmailVerification.Commands.Confirm;
using Backend.Veteriner.Application.EmailVerification.Commands.Request;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class EmailController : ControllerBase
{
    private readonly IMediator _mediator;
    public EmailController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Verilen e-posta için doğrulama e-postası gönderir (outbox üzerinden).
    /// </summary>
    [EnableRateLimiting("email-verify-request")]
    [AllowAnonymous]
    [HttpPost("request-verification")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestVerification([FromBody] RequestEmailVerificationCommand cmd, CancellationToken ct)
    {
        await _mediator.Send(cmd, ct);
        return Ok(new { ok = true });
    }

    /// <summary>
    /// E-postadaki linkten gelen doğrulama (URL üzerinden token).
    /// Örn: GET /api/v1/email/confirm?token=RAW_TOKEN
    /// </summary>
    [AllowAnonymous]
    [HttpGet("confirm")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Confirm([FromQuery] string token, CancellationToken ct)
    {
        await _mediator.Send(new ConfirmEmailVerificationCommand(token), ct);
        return Ok(new { ok = true, message = "E-posta doğrulandı." });
    }

    /// <summary>
    /// (Opsiyonel) Linke tıklamak zorunda kalmadan POST ile de onaylayabilmek için alternatif uç.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("confirm")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmPost([FromBody] ConfirmEmailVerificationCommand cmd, CancellationToken ct)
    {
        await _mediator.Send(cmd, ct);
        return Ok(new { ok = true, message = "E-posta doğrulandı." });
    }
}
