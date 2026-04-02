using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Auth.Commands.Logout;
using Backend.Veteriner.Application.Auth.Commands.LogoutAll;
using Backend.Veteriner.Application.Auth.Commands.Refresh;
using Backend.Veteriner.Application.Auth.Commands.SelectClinic;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>Kimlik doğrulama ve oturum (login/refresh ayrı sözleşme: <c>docs/AUTH_TENANT_CONTRACT.md</c>).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <summary>Giriş. Gövde: email, password, isteğe bağlı tenantId (çok kiracıda zorunlu). Swagger şeması <see cref="LoginCommand"/>.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginCommand? command, CancellationToken ct)
    {
        if (command is null)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Validation.InvalidRequestBody",
                "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        Result<LoginResultDto> result = await _mediator.Send(command, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Token yenileme. Gövde: yalnızca refreshToken (kiracı istekte taşınmaz).</summary>
    [AllowAnonymous]
    [EnableRateLimiting("refresh")]
    [HttpPost("refresh")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Refresh([FromBody] RefreshCommand? cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd?.RefreshToken))
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Validation.RefreshTokenRequired",
                "refreshToken zorunludur.")
                .ToActionResult(this);
        }

        Result<LoginResultDto> result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Aktif klinik seçimi. Body: refreshToken + clinicId. Başarılı olunca clinic_id claim içeren yeni access/refresh döner.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("select-clinic")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SelectClinic([FromBody] SelectClinicCommand? cmd, CancellationToken ct)
    {
        if (cmd is null || string.IsNullOrWhiteSpace(cmd.RefreshToken) || cmd.ClinicId == Guid.Empty)
        {
            return Result<LoginResultDto>.Failure(
                "Auth.Validation.SelectClinicRequestInvalid",
                "refreshToken ve clinicId zorunludur.")
                .ToActionResult(this);
        }

        Result<LoginResultDto> result = await _mediator.Send(cmd, ct);
        return result.ToActionResult(this);
    }

    [Authorize]
    [HttpPost("logout")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AuthActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] AuthLogoutBodyDto? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.RefreshToken))
        {
            return Result<AuthActionResultDto>.Success(
                    new AuthActionResultDto(true, "Refresh token yok (no-op)."))
                .ToActionResult(this);
        }

        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out _))
            return Result.Failure("Auth.Unauthorized.UserContextMissing", "Kullanici kimligi token icinde bulunamadi.")
                .ToActionResult(this);

        await _mediator.Send(new LogoutCommand(body.RefreshToken), ct);
        return Result<AuthActionResultDto>.Success(new AuthActionResultDto(true)).ToActionResult(this);
    }

    [Authorize]
    [HttpPost("logout-all")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AuthActionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
            return Result.Failure("Auth.Unauthorized.UserContextMissing", "Kullanici kimligi token icinde bulunamadi.")
                .ToActionResult(this);

        await _mediator.Send(new LogoutAllCommand(userId), ct);
        return Result<AuthActionResultDto>.Success(new AuthActionResultDto(true)).ToActionResult(this);
    }
}
