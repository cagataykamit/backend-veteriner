using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Outbox;

[CollectionDefinition("outbox-processor", DisableParallelization = true)]
public sealed class OutboxProcessorCollection;

/// <summary>
/// Ayrı LocalDB + no-op SMTP ile OutboxProcessor gerçek işleme döngüsünü doğrular.
/// </summary>
[Collection("outbox-processor")]
public sealed class OutboxProcessorIntegrationTests : IClassFixture<OutboxProcessorWebApplicationFactory>
{
    private readonly OutboxProcessorWebApplicationFactory _factory;

    public OutboxProcessorIntegrationTests(OutboxProcessorWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task OutboxProcessor_Should_MarkEmailMessageProcessed_WhenImmediateSenderSucceeds()
    {
        var subjectMarker = $"proc-{Guid.NewGuid():N}";

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            await outbox.EnqueueEmailAsync(new EmailOutboxPayload
            {
                To = "noop@example.com",
                Subject = subjectMarker,
                Body = "integration"
            });
        }

        OutboxMessage? row = null;
        await using (var pollScope = _factory.Services.CreateAsyncScope())
        {
            var db = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                row = await db.OutboxMessages.AsNoTracking()
                    .Where(m => m.Type == OutboxMessageTypes.Email && m.Payload.Contains(subjectMarker))
                    .OrderByDescending(m => m.CreatedAtUtc)
                    .FirstOrDefaultAsync();
                if (row?.ProcessedAtUtc is not null)
                    break;
            }
        }

        row.Should().NotBeNull();
        row!.Type.Should().Be(OutboxMessageTypes.Email, "Outbox email mesaj tipleri canonical olarak üretilmeli");
        row!.ProcessedAtUtc.Should().NotBeNull("OutboxProcessor should set ProcessedAtUtc after successful email handling");
        row.LastError.Should().BeNull();
        row.Error.Should().BeNull();
        row.DeadLetterAtUtc.Should().BeNull();

        await using (var cleanup = _factory.Services.CreateAsyncScope())
        {
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.OutboxMessages.FirstAsync(x => x.Id == row.Id);
            db.OutboxMessages.Remove(tracked);
            await db.SaveChangesAsync();
        }
    }
}
