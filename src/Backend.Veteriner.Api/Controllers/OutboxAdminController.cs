using Asp.Versioning;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Contracts.Outbox;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Outbox;
using Backend.Veteriner.Application.Outbox.Commands.Delete;
using Backend.Veteriner.Application.Outbox.Commands.RetryAllDeadLetters;
using Backend.Veteriner.Application.Outbox.Commands.RetryDeadLetter;
using Backend.Veteriner.Application.Outbox.Queries.GetDeadLettersPaged;
using Backend.Veteriner.Application.Outbox.Queries.GetPendingPaged;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/outbox")]
[Authorize(Policy = PermissionCatalog.Outbox.Read)]
[Produces("application/json")]
public sealed class OutboxAdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public OutboxAdminController(IMediator mediator) => _mediator = mediator;

    // GET /api/v1/admin/outbox/pending?page=1&pageSize=50&sort=createdAtUtc&order=desc&search=...
    [HttpGet("pending")]
    public async Task<ActionResult<PagedResult<OutboxItemDto>>> GetPending([FromQuery] PageRequest req, CancellationToken ct)
        => Ok(await _mediator.Send(new GetOutboxPendingPagedQuery(req), ct));

    // GET /api/v1/admin/outbox/dead?page=1&pageSize=50&sort=deadLetterAtUtc&order=desc&search=...
    [HttpGet("dead")]
    public async Task<ActionResult<PagedResult<OutboxItemDto>>> GetDead([FromQuery] PageRequest req, CancellationToken ct)
        => Ok(await _mediator.Send(new GetOutboxDeadLettersPagedQuery(req), ct));

    // POST /api/v1/admin/outbox/retry/{id}
    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpPost("retry/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RetryOutboxDeadLetterCommand(id), ct);
        if (result.IsSuccess) return Ok(new { ok = true });

        // Bu uçlar ResultExtensions şeklini kullanmaz; mevcut yanıtlar (NotFound(), "Invalid operation" 400) korunur.
        if (result.Error.Code == OutboxErrors.NotDeadLetter.Code)
        {
            return Problem(
                title: "Invalid operation",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        return NotFound();
    }

    // POST /api/v1/admin/outbox/retry-dead-all
    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpPost("retry-dead-all")]
    public async Task<IActionResult> RetryAllDead(CancellationToken ct)
    {
        var count = await _mediator.Send(new RetryAllOutboxDeadLettersCommand(), ct);
        return Ok(new { count });
    }

    // DELETE /api/v1/admin/outbox/{id}
    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteOutboxMessageCommand(id), ct);
        return result.IsSuccess ? NoContent() : NotFound();
    }
}
