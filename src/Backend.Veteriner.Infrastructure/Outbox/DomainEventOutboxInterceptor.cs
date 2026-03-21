using System.Diagnostics;
using System.Text.Json;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// <summary>
/// Aggregate root'larda biriken domain event'leri SaveChanges öncesi Outbox tablosuna yazar.
/// Böylece event publish işlemi request içinde kaybolmaz; retry / dead-letter mekanizmasına girer.
/// </summary>
public sealed class DomainEventOutboxInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var db = eventData.Context;
        if (db is null)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        // Domain event taşıyan aggregate root'ları bul
        var aggregates = db.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (aggregates.Count == 0)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        var outboxMessages = new List<OutboxMessage>();

        // Mevcut request trace bilgisi
        var traceId = Activity.Current?.TraceId.ToString();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var outboxMessage = new OutboxMessage
                {
                    Id = Guid.NewGuid(),

                    // Domain event CLR type adı
                    // OutboxProcessor tarafında registry ile çözülecek
                    Type = domainEvent.GetType().FullName!
,
                    // Event payload
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions),

                    CreatedAtUtc = DateTime.UtcNow,

                    RetryCount = 0,
                    ProcessedAtUtc = null,
                    NextAttemptAtUtc = null,
                    DeadLetterAtUtc = null,
                    LastError = null,
                    Error = null,

                    // İsterseniz correlation id'yi başka middleware'den de besleyebilirsiniz
                    CorrelationId = null,
                    TraceId = traceId
                };

                outboxMessages.Add(outboxMessage);
            }

            // Aynı event'in ikinci kez yazılmaması için aggregate üzerindeki olayları temizle
            aggregate.ClearDomainEvents();
        }

        db.Set<OutboxMessage>().AddRange(outboxMessages);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}