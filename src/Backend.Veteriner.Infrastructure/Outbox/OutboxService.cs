using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;

namespace Backend.Veteriner.Infrastructure.Outbox;

public sealed class OutboxService : IOutbox
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public OutboxService(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var msg = new OutboxMessage
        {
            Type = type,
            Payload = payloadJson,
            CreatedAtUtc = now,
            RetryCount = 0,
            NextAttemptAtUtc = now // ilk deneme hemen
        };
        await _db.OutboxMessages.AddAsync(msg, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task EnqueueEmailAsync(EmailOutboxPayload email, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(email);
        return EnqueueAsync(OutboxMessageTypes.Email, json, ct);
    }
}
