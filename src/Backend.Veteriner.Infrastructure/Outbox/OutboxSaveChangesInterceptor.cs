// src/Backend.Veteriner.Infrastructure/Outbox/OutboxSaveChangesInterceptor.cs
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Outbox;

public sealed class OutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly OutboxBuffer _buffer; // somut kullan�yoruz ki Drain eri�ilsin
    private readonly ILogger<OutboxSaveChangesInterceptor> _logger;

    public OutboxSaveChangesInterceptor(OutboxBuffer buffer, ILogger<OutboxSaveChangesInterceptor> logger)
    {
        _buffer = buffer;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AppendOutbox(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AppendOutbox(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AppendOutbox(DbContextEventData eventData)
    {
        if (eventData.Context is not AppDbContext db)
        {
            _logger.LogDebug("OutboxInterceptor: context is not AppDbContext, skipping.");
            return;
        }

        var batch = _buffer.Drain();
        if (batch.Count == 0)
        {
            _logger.LogDebug("OutboxInterceptor: no buffered message for this SaveChanges.");
            return;
        }

        _logger.LogInformation("OutboxInterceptor: drained {Count} item(s).", batch.Count);

        var now = DateTime.UtcNow;
        foreach (var item in batch)
        {
            _logger.LogDebug("OutboxInterceptor: append type={Type}, len={Len}", item.Type, item.Payload?.Length ?? 0);
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = item.Type,
                Payload = item.Payload ?? string.Empty,
                CreatedAtUtc = now,
                RetryCount = 0,
                NextAttemptAtUtc = now
            });
        }

        _logger.LogInformation("OutboxInterceptor: appended {Count} outbox message(s).", batch.Count);
    }
}
