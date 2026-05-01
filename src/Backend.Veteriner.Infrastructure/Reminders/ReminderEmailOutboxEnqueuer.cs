using System.Text.Json;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;

namespace Backend.Veteriner.Infrastructure.Reminders;

public sealed class ReminderEmailOutboxEnqueuer : IReminderEmailOutboxEnqueuer
{
    private readonly AppDbContext _db;

    public ReminderEmailOutboxEnqueuer(AppDbContext db)
    {
        _db = db;
    }

    public Task<Guid> EnqueueReminderEmailAsync(
        string to,
        string subject,
        string body,
        bool isHtml,
        CancellationToken ct)
    {
        var payload = new EmailOutboxPayload
        {
            To = to,
            Subject = subject,
            Body = body,
            IsHtml = isHtml
        };

        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = OutboxMessageTypes.Email,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAtUtc = DateTime.UtcNow,
            RetryCount = 0
        };

        _db.OutboxMessages.Add(message);
        return Task.FromResult(message.Id);
    }
}

