using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Api.Contracts;
using Backend.Veteriner.Application.Public.Commands.OwnerSignup;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
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
}
