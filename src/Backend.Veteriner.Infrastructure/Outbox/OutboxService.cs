using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;

namespace Backend.Veteriner.Infrastructure.Outbox;

public sealed class OutboxService : IOutbox
{
    private readonly AppDbContext _db;

    public OutboxService(AppDbContext db) => _db = db;

    public async Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default)
    {
        var msg = new OutboxMessage
        {
            Type = type,
            Payload = payloadJson,
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0,
            NextAttemptAtUtc = DateTime.UtcNow // ilk deneme hemen
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
