using Backend.Veteriner.Api.Common.Extensions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Users.Commands.Create;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Queries.GetAll;
using Backend.Veteriner.Application.Users.Queries.GetById;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users")]
[Produces("application/json")]
public sealed class UsersAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersAdminController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Admin: kullanıcıları sayfalı listeler.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Users.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AdminUserListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] PageRequest req, CancellationToken ct)
        => Ok(await _mediator.Send(new AdminGetUsersQuery(req), ct));

    /// <summary>
    /// Admin: kullanıcı detay.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Users.Read)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminUserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new AdminGetUserByIdQuery(id), ct);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Admin: kullanıcı oluşturur.
    /// </summary>
    [Authorize(Policy = PermissionCatalog.Users.Write)]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] AdminCreateUserCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);

        if (!result.IsSuccess)
        {
            return result.ToActionResult(this);
        }

        var id = result.Value;

        return CreatedAtAction(
            nameof(GetById),
            new { version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0", id },
            id
        );
    }
}
