using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Add;
using Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Remove;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/claims/{claimId:guid}/permissions")]
[Authorize(Policy = PermissionCatalog.Roles.Write)]
public sealed class OperationClaimPermissionsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<OperationClaimPermissionsController> _logger;

    public OperationClaimPermissionsController(IMediator mediator, ILogger<OperationClaimPermissionsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("{permissionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Add(Guid claimId, Guid permissionId, CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();
        _logger.LogInformation(
            "AUDIT Roles.Write AddPermissionToClaim actorUserId={ActorUserId} claimId={ClaimId} permissionId={PermissionId}",
            actorUserId, claimId, permissionId);

        await _mediator.Send(new AddPermissionToClaimCommand(claimId, permissionId), ct);

        _logger.LogInformation(
            "AUDIT Roles.Write AddPermissionToClaim SUCCESS actorUserId={ActorUserId} claimId={ClaimId} permissionId={PermissionId}",
            actorUserId, claimId, permissionId);

        return NoContent();
    }

    [HttpDelete("{permissionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid claimId, Guid permissionId, CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();
        _logger.LogInformation(
            "AUDIT Roles.Write RemovePermissionFromClaim actorUserId={ActorUserId} claimId={ClaimId} permissionId={PermissionId}",
            actorUserId, claimId, permissionId);

        await _mediator.Send(new RemovePermissionFromClaimCommand(claimId, permissionId), ct);

        _logger.LogInformation(
            "AUDIT Roles.Write RemovePermissionFromClaim SUCCESS actorUserId={ActorUserId} claimId={ClaimId} permissionId={PermissionId}",
            actorUserId, claimId, permissionId);

        return NoContent();
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
