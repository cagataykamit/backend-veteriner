using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Outbox.Commands.Requeue;
using Backend.Veteriner.Application.Outbox.Queries.GetDeadLetters;
using Backend.Veteriner.Application.Outbox.Queries.GetPending;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = PermissionCatalog.Outbox.Read)]
public sealed class OutboxController : ControllerBase
{
    private readonly IMediator _mediator;
    public OutboxController(IMediator mediator) => _mediator = mediator;

    [HttpGet("pending")]
    [Produces("application/json")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
        => Ok(await _mediator.Send(new GetOutboxPendingQuery(), ct));

    [HttpGet("dead")]
    [Produces("application/json")]
    public async Task<IActionResult> GetDead(CancellationToken ct)
        => Ok(await _mediator.Send(new GetOutboxDeadLettersQuery(), ct));

    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpPost("requeue/{id:guid}")]
    [Produces("application/json")]
    public async Task<IActionResult> Requeue(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new RequeueOutboxMessageCommand(id), ct);
        if (!result.IsSuccess) return NotFound();

        return Ok(new { ok = true });
    }
}
