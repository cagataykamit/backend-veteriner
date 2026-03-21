using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetByUserId;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users/{userId:guid}/permissions")]
[Authorize(Policy = PermissionCatalog.Permissions.Read)]
[Produces("application/json")]
public sealed class UserPermissionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserPermissionsController(IMediator mediator)
        => _mediator = mediator;

    /// <summary>
    /// Admin amaçlı: kullanıcının efektif permission kodlarını listeler.
    /// (User -> UserOperationClaim -> OperationClaimPermission -> Permission(Code))
    /// </summary>
    [HttpGet("effective")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEffective(Guid userId, CancellationToken ct)
    {
        var list = await _mediator.Send(new GetPermissionsByUserIdQuery(userId), ct);
        return Ok(list);
    }
}
