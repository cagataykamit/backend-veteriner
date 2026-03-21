using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using System.Text.Json;

namespace Backend.Veteriner.Infrastructure.Outbox;

public sealed class EfOutbox : IOutbox
{
    private readonly AppDbContext _db;
    public EfOutbox(AppDbContext db) => _db = db;

    public async Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default)
    {
        var msg = new OutboxMessage
        {
            Type = type,
            Payload = payloadJson,
            CreatedAtUtc = DateTime.UtcNow,
            NextAttemptAtUtc = DateTime.UtcNow
        };

        await _db.OutboxMessages.AddAsync(msg, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task EnqueueEmailAsync(EmailOutboxPayload email, CancellationToken ct = default)
        => EnqueueAsync(OutboxMessageTypes.Email, JsonSerializer.Serialize(email), ct);
}
