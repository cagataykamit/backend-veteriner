using System.Security.Claims;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Public.Commands.AcceptInvite;
using Backend.Veteriner.Application.Public.Commands.OwnerSignup;
using Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Application.Public.Queries.InviteDetail;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/public")]
[Produces("application/json")]
public sealed class PublicController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Public owner signup: tek istekte owner user + tenant + clinic + trial subscription başlangıcı oluşturur.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("owner-signup")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(PublicOwnerSignupResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OwnerSignup([FromBody] PublicOwnerSignupBody? body, CancellationToken ct)
    {
        if (body is null)
        {
            return Result<PublicOwnerSignupResultDto>.Failure(
                    "PublicOwnerSignup.Validation.InvalidRequestBody",
                    "Istek govdesi bos veya hatali JSON.")
                .ToActionResult(this);
        }

        var command = new PublicOwnerSignupCommand(
            body.PlanCode,
            body.TenantName,
            body.ClinicName,
            body.ClinicCity,
            body.Email,
            body.Password);

        var result = await _mediator.Send(command, ct);
        return result.ToActionResult(this);
    }

    /// <summary>Public davet doğrulama (join ekranı).</summary>
    [AllowAnonymous]
    [HttpGet("invites/{token}")]
    [ProducesResponseType(typeof(PublicTenantInviteDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInviteDetail([FromRoute] string token, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPublicTenantInviteDetailQuery(token), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Oturum açık kullanıcı daveti kabul eder (e-posta davet ile eşleşmeli).</summary>
    [Authorize]
    [HttpPost("invites/{token}/accept")]
    [ProducesResponseType(typeof(TenantInviteAcceptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcceptInvite([FromRoute] string token, CancellationToken ct)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                    "Auth.Unauthorized.UserContextMissing",
                    "Kullanici kimligi token icinde bulunamadi.")
                .ToActionResult(this);
        }

        var result = await _mediator.Send(new AcceptTenantInviteCommand(token, userId), ct);
        return result.ToActionResult(this);
    }

    /// <summary>Yeni hesap oluşturup daveti tek adımda kabul eder.</summary>
    [AllowAnonymous]
    [HttpPost("invites/{token}/signup-and-accept")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TenantInviteAcceptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SignupAndAcceptInvite([FromRoute] string token, [FromBody] SignupAndAcceptInviteBody? body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Password))
        {
            return Result<TenantInviteAcceptResultDto>.Failure(
                    "Invites.Validation.InvalidRequestBody",
                    "password zorunludur.")
                .ToActionResult(this);
        }

        var result = await _mediator.Send(new SignupAndAcceptTenantInviteCommand(token, body.Password), ct);
        return result.ToActionResult(this);
    }
}
