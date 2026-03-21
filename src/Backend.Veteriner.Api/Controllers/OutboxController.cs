using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize(Policy = PermissionCatalog.Outbox.Read)]
public sealed class OutboxController : ControllerBase
{
    private readonly AppDbContext _db;
    public OutboxController(AppDbContext db) => _db = db;

    [HttpGet("pending")]
    [Produces("application/json")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var items = await _db.OutboxMessages
            .Where(m => m.ProcessedAtUtc == null && m.DeadLetterAtUtc == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(100)
            .Select(m => new
            {
                m.Id,
                m.Type,
                m.RetryCount,
                m.CreatedAtUtc,
                m.NextAttemptAtUtc,
                m.LastError
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpGet("dead")]
    [Produces("application/json")]
    public async Task<IActionResult> GetDead(CancellationToken ct)
    {
        var items = await _db.OutboxMessages
            .Where(m => m.DeadLetterAtUtc != null && m.ProcessedAtUtc == null)
            .OrderByDescending(m => m.DeadLetterAtUtc)
            .Take(100)
            .Select(m => new
            {
                m.Id,
                m.Type,
                m.RetryCount,
                m.CreatedAtUtc,
                m.DeadLetterAtUtc,
                m.LastError
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [Authorize(Policy = PermissionCatalog.Outbox.Write)]
    [HttpPost("requeue/{id:guid}")]
    [Produces("application/json")]
    public async Task<IActionResult> Requeue(Guid id, CancellationToken ct)
    {
        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (msg is null) return NotFound();

        msg.DeadLetterAtUtc = null;
        msg.NextAttemptAtUtc = DateTime.UtcNow;
        msg.LastError = null;
        msg.Error = null;
        msg.RetryCount = 0;

        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
