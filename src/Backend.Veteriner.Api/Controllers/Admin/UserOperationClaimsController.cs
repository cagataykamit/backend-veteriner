using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Assign;
using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;
using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetByUserId;
using Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetDetails;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users/{userId:guid}/operation-claims")]
[Produces("application/json")]
public sealed class UserOperationClaimsController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserOperationClaimsController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Kullanıcının claim/rol ilişkilerini listeler (ham).
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Roles.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserOperationClaimDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(Guid userId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetUserOperationClaimsByUserIdQuery(userId), ct));

    /// <summary>
    /// Kullanıcının claim/rol ilişkilerini listeler (detay).
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Roles.Read)]
    [HttpGet("details")]
    [ProducesResponseType(typeof(IReadOnlyList<UserOperationClaimDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDetails(Guid userId, CancellationToken ct)
        => Ok(await _mediator.Send(new GetUserOperationClaimDetailsByUserIdQuery(userId), ct));

    /// <summary>
    /// Kullanıcıya OperationClaim (rol) atar.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Roles.Write)]
    [HttpPost("{claimId:guid}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign(Guid userId, Guid claimId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AssignUserOperationClaimCommand(userId, claimId), ct);
        if (!result.IsSuccess)
            return result.ToActionResult(this);
        return StatusCode(StatusCodes.Status201Created, new { id = result.Value });
    }

    /// <summary>
    /// Kullanıcıdan OperationClaim (rol) kaldırır.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Roles.Write)]
    [HttpDelete("{claimId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid userId, Guid claimId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveUserOperationClaimCommand(userId, claimId), ct);
        return NoContent();
    }
}
