using Backend.Veteriner.Application.Common.Contracts.Outbox;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Outbox;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Outbox;

public sealed class OutboxAdminRepository : IOutboxAdminRepository
{
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;

    public OutboxAdminRepository(AppDbContext db) => _db = db;

    public Task<PagedResult<OutboxItemDto>> GetPendingPagedAsync(PageRequest req, CancellationToken ct)
        => GetPagedAsync(_db.OutboxMessages.Where(x => x.ProcessedAtUtc == null && x.DeadLetterAtUtc == null), req, ct);

    public Task<PagedResult<OutboxItemDto>> GetDeadLettersPagedAsync(PageRequest req, CancellationToken ct)
        => GetPagedAsync(_db.OutboxMessages.Where(x => x.DeadLetterAtUtc != null && x.ProcessedAtUtc == null), req, ct);

    public async Task<OutboxRetryOutcome> RetryDeadLetterAsync(Guid id, CancellationToken ct)
    {
        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (msg is null) return OutboxRetryOutcome.NotFound;
        if (msg.DeadLetterAtUtc is null) return OutboxRetryOutcome.NotDeadLetter;

        msg.DeadLetterAtUtc = null;
        msg.NextAttemptAtUtc = DateTime.UtcNow;
        msg.LastError = null;
        return OutboxRetryOutcome.Retried;
    }

    public async Task<int> RetryAllDeadLettersAsync(CancellationToken ct)
    {
        var list = await _db.OutboxMessages
            .Where(x => x.DeadLetterAtUtc != null && x.ProcessedAtUtc == null)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        foreach (var m in list)
        {
            m.DeadLetterAtUtc = null;
            m.NextAttemptAtUtc = now;
            m.LastError = null;
        }

        return list.Count;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken ct)
    {
        var msg = await _db.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (msg is null) return false;

        _db.OutboxMessages.Remove(msg);
        return true;
    }

    private static async Task<PagedResult<OutboxItemDto>> GetPagedAsync(
        IQueryable<OutboxMessage> q, PageRequest req, CancellationToken ct)
    {
        var page = Math.Max(1, req.Page);
        var pageSize = Math.Clamp(req.PageSize, 1, MaxPageSize);

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

        return PagedResult<OutboxItemDto>.Create(items, total, page, pageSize);
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
}
