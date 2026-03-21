using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Contracts.Outbox;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/outbox")]
[Authorize(Policy = PermissionCatalog.Outbox.Read)]
[Produces("application/json")]
public sealed class OutboxAdminController : ControllerBase
{
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;
    private readonly ILogger<OutboxAdminController> _logger;

    public OutboxAdminController(AppDbContext db, ILogger<OutboxAdminController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // GET /api/v1/admin/outbox/pending?page=1&pageSize=50&sort=createdAtUtc&order=desc&search=...
    [HttpGet("pending")]
    public async Task<ActionResult<PagedResult<OutboxItemDto>>> GetPending([FromQuery] PageRequest req, CancellationToken ct)
    {
        var (page, pageSize) = Normalize(req);

        IQueryable<OutboxMessage> q = _db.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null && x.DeadLetterAtUtc == null);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim();
            q = q.Where(x => x.Type.Contains(s));
        }

        q = ApplySort(q, req.Sort, req.Order);

        var total = await q.CountAsync(ct);

        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new OutboxItemDto
            {
                Id = x.Id,
                Type = x.Type,
                CreatedAtUtc = x.CreatedAtUtc,
                RetryCount = x.RetryCount,
                NextAttemptAtUtc = x.NextAttemptAtUtc,
                LastError = x.LastError,
                DeadLetterAtUtc = x.DeadLetterAtUtc
            })
            .ToListAsync(ct);

        return Ok(PagedResult<OutboxItemDto>.Create(items, total, page, pageSize));
    }

    // GET /api/v1/admin/outbox/dead?page=1&pageSize=50&sort=deadLetterAtUtc&order=desc&search=...
    [HttpGet("dead")]
    public async Task<ActionResult<PagedResult<OutboxItemDto>>> GetDead([FromQuery] PageRequest req, CancellationToken ct)
    {
        var (page, pageSize) = Normalize(req);

        IQueryable<OutboxMessage> q = _db.OutboxMessages
            .Where(x => x.DeadLetterAtUtc != null && x.ProcessedAtUtc == null);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim();
            q = q.Where(x => x.Type.Contains(s));
        }

        q = ApplySort(q, req.Sort, req.Order);

        var total = await q.CountAsync(ct);

        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new OutboxItemDto
            {
                Id = x.Id,
                Type = x.Type,
                CreatedAtUtc = x.CreatedAtUtc,
                RetryCount = x.RetryCount,
                NextAttemptAtUtc = x.NextAttemptAtUtc,
                LastError = x.LastError,
                DeadLetterAtUtc = x.DeadLetterAtUtc
            })
            .ToListAsync(ct);

        return Ok(PagedResult<OutboxItemDto>.Create(items, total, page, pageSize));
    }

    // POST /api/v1/admin/outbox/retry/{id}
    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpPost("retry/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryDeadLetter actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, id);

        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (msg is null)
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write RetryDeadLetter NOT_FOUND actorUserId={ActorUserId} outboxId={OutboxId}",
                actorUserId, id);
            return NotFound();
        }

        if (msg.DeadLetterAtUtc is null)
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write RetryDeadLetter INVALID_OPERATION actorUserId={ActorUserId} outboxId={OutboxId} reason=not_dead_letter",
                actorUserId, id);

            return Problem(
                title: "Invalid operation",
                detail: "Mesaj dead-letter de�il.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        msg.DeadLetterAtUtc = null;
        msg.NextAttemptAtUtc = DateTime.UtcNow;
        msg.LastError = null;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryDeadLetter SUCCESS actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, id);

        return Ok(new { ok = true });
    }

    // POST /api/v1/admin/outbox/retry-dead-all
    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpPost("retry-dead-all")]
    public async Task<IActionResult> RetryAllDead(CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryAllDeadLetter actorUserId={ActorUserId}",
            actorUserId);

        var list = await _db.OutboxMessages
            .Where(x => x.DeadLetterAtUtc != null && x.ProcessedAtUtc == null)
            .ToListAsync(ct);

        if (list.Count == 0)
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write RetryAllDeadLetter SUCCESS actorUserId={ActorUserId} count=0",
                actorUserId);

            return Ok(new { count = 0 });
        }

        var now = DateTime.UtcNow;

        foreach (var m in list)
        {
            m.DeadLetterAtUtc = null;
            m.NextAttemptAtUtc = now;
            m.LastError = null;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AUDIT Outbox.Write RetryAllDeadLetter SUCCESS actorUserId={ActorUserId} count={Count}",
            actorUserId, list.Count);

        return Ok(new { count = list.Count });
    }

    // DELETE /api/v1/admin/outbox/{id}
    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var actorUserId = GetActorUserIdOrNull();

        _logger.LogInformation(
            "AUDIT Outbox.Write DeleteOutboxMessage actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, id);

        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (msg is null)
        {
            _logger.LogInformation(
                "AUDIT Outbox.Write DeleteOutboxMessage NOT_FOUND actorUserId={ActorUserId} outboxId={OutboxId}",
                actorUserId, id);

            return NotFound();
        }

        _db.OutboxMessages.Remove(msg);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AUDIT Outbox.Write DeleteOutboxMessage SUCCESS actorUserId={ActorUserId} outboxId={OutboxId}",
            actorUserId, id);

        return NoContent();
    }

    private static (int page, int pageSize) Normalize(PageRequest req)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);
        return (page, pageSize);
    }

    private static IQueryable<OutboxMessage> ApplySort(IQueryable<OutboxMessage> q, string? sort, string? order)
    {
        var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

        return (sort?.Trim().ToLowerInvariant()) switch
        {
            "createdatutc" => desc ? q.OrderByDescending(x => x.CreatedAtUtc) : q.OrderBy(x => x.CreatedAtUtc),
            "retrycount" => desc ? q.OrderByDescending(x => x.RetryCount) : q.OrderBy(x => x.RetryCount),
            "type" => desc ? q.OrderByDescending(x => x.Type) : q.OrderBy(x => x.Type),
            "deadletteratutc" => desc ? q.OrderByDescending(x => x.DeadLetterAtUtc) : q.OrderBy(x => x.DeadLetterAtUtc),
            _ => q.OrderByDescending(x => x.CreatedAtUtc)
        };
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
