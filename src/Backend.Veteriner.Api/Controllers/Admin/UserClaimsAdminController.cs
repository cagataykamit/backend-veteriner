using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Commands.Claims.Add;
using Backend.Veteriner.Application.Users.Commands.Claims.Remove;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Queries.Claims.GetByUserId;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users/{userId:guid}/claims")]
[Produces("application/json")]
public sealed class UserClaimsAdminController : ControllerBase
{
    private readonly IMediator _mediator;
    public UserClaimsAdminController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Admin: kullanıcının rollerini listeler.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Users.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserOperationClaimDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
        => Ok(await _mediator.Send(new AdminGetUserClaimsQuery(userId), ct));

    /// <summary>
    /// Admin: kullanıcıya rol atar.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Users.Write)]
    [HttpPost("{claimId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Add(Guid userId, Guid claimId, CancellationToken ct)
    {
        await _mediator.Send(new AdminAddUserClaimCommand(userId, claimId), ct);
        return NoContent();
    }

    /// <summary>
    /// Admin: kullanıcıdan rol çıkarır.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Users.Write)]
    [HttpDelete("{claimId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid userId, Guid claimId, CancellationToken ct)
    {
        await _mediator.Send(new AdminRemoveUserClaimCommand(userId, claimId), ct);
        return NoContent();
    }
}
