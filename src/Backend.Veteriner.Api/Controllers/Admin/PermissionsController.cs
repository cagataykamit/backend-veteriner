using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Commands.Permissions.Create;
using Backend.Veteriner.Application.Auth.Commands.Permissions.Delete;
using Backend.Veteriner.Application.Auth.Commands.Permissions.Update;
using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetAll;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetById;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/permissions")]
[Authorize(Policy = PermissionCatalog.Permissions.Read)]
[Produces("application/json")]
public sealed class PermissionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PermissionsController> _logger;

    public PermissionsController(IMediator mediator, ILogger<PermissionsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PermissionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PermissionDto>>> GetAll([FromQuery] PageRequest req, CancellationToken ct)
        => Ok(await _mediator.Send(new GetAllPermissionsQuery(req), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PermissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPermissionByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    [Authorize(Policy = PermissionCatalog.Permissions.Write)]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePermissionCommand cmd, CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();
        _logger.LogInformation(
            "AUDIT Permissions.Write CreatePermission actorUserId={ActorUserId}",
            actorUserId);

        Result<Guid> result = await _mediator.Send(cmd, ct);

        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        var id = result.Value;

        _logger.LogInformation(
            "AUDIT Permissions.Write CreatePermission SUCCESS actorUserId={ActorUserId} permissionId={PermissionId}",
            actorUserId, id);

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id
        );
    }

    [Authorize(Policy = PermissionCatalog.Permissions.Write)]
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePermissionCommand body, CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();
        _logger.LogInformation(
            "AUDIT Permissions.Write UpdatePermission actorUserId={ActorUserId} permissionId={PermissionId}",
            actorUserId, id);

        var result = await _mediator.Send(body with { Id = id }, ct);

        _logger.LogInformation(
            "AUDIT Permissions.Write UpdatePermission SUCCESS actorUserId={ActorUserId} permissionId={PermissionId}",
            actorUserId, id);

        return result.ToActionResult(this);
    }

    [Authorize(Policy = PermissionCatalog.Permissions.Write)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();
        _logger.LogInformation(
            "AUDIT Permissions.Write DeletePermission actorUserId={ActorUserId} permissionId={PermissionId}",
            actorUserId, id);

        var result = await _mediator.Send(new DeletePermissionCommand(id), ct);

        _logger.LogInformation(
            "AUDIT Permissions.Write DeletePermission SUCCESS actorUserId={ActorUserId} permissionId={PermissionId}",
            actorUserId, id);

        return result.ToActionResult(this);
    }

    private Guid? GetActorUserIdOrNull()
    {
        var sub =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub") ??
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
