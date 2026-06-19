using System.Text.Json;
using Backend.Veteriner.Application.Appointments.IntegrationEvents;
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

    [Fact]
    public async Task OutboxProcessor_Should_NotConsumeKnownAppointmentIntegrationEvents()
    {
        Guid messageId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var snapshot = new AppointmentProjectionSnapshot(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Klinik",
                Guid.NewGuid(),
                "Pet",
                Guid.NewGuid(),
                "Tur",
                Guid.NewGuid(),
                "Client",
                null,
                DateTime.UtcNow,
                30,
                0,
                0,
                null);

            var payload = JsonSerializer.Serialize(
                new AppointmentCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, 1L, snapshot),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            var message = new OutboxMessage
            {
                Type = AppointmentIntegrationEventTypes.Created,
                Payload = payload,
                CreatedAtUtc = DateTime.UtcNow
            };
            commandDb.OutboxMessages.Add(message);
            await commandDb.SaveChangesAsync();
            messageId = message.Id;
        }

        OutboxMessage? row = null;
        await using (var pollScope = _factory.Services.CreateAsyncScope())
        {
            var db = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500);
                row = await db.OutboxMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId);
                if (row is not null && row.RetryCount > 0)
                    break;
            }
        }

        row.Should().NotBeNull();
        row!.ProcessedAtUtc.Should().BeNull("appointment integration eventleri OutboxProcessor tarafindan tuketilmemeli");
        row.DeadLetterAtUtc.Should().BeNull();
        row.RetryCount.Should().Be(0);

        await using (var cleanup = _factory.Services.CreateAsyncScope())
        {
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.OutboxMessages.FirstAsync(x => x.Id == messageId);
            db.OutboxMessages.Remove(tracked);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task OutboxProcessor_Should_RetryUnknownAppointmentLikeType()
    {
        const string unknownType = "appointment.unknown.v1";
        Guid messageId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = new OutboxMessage
            {
                Type = unknownType,
                Payload = "{}",
                CreatedAtUtc = DateTime.UtcNow
            };
            commandDb.OutboxMessages.Add(message);
            await commandDb.SaveChangesAsync();
            messageId = message.Id;
        }

        OutboxMessage? row = null;
        await using (var pollScope = _factory.Services.CreateAsyncScope())
        {
            var db = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500);
                row = await db.OutboxMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId);
                if (row?.RetryCount > 0)
                    break;
            }
        }

        row.Should().NotBeNull();
        row!.RetryCount.Should().BeGreaterThan(0);
        row.ProcessedAtUtc.Should().BeNull();
        row.LastError.Should().NotBeNullOrWhiteSpace();

        await using (var cleanup = _factory.Services.CreateAsyncScope())
        {
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.OutboxMessages.FirstAsync(x => x.Id == messageId);
            db.OutboxMessages.Remove(tracked);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task OutboxProcessor_Should_ProcessEmailBehindManyAppointmentMessages()
    {
        var subjectMarker = $"starve-{Guid.NewGuid():N}";
        Guid emailMessageId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseTime = DateTime.UtcNow.AddHours(-2);

            for (var i = 0; i < 55; i++)
            {
                commandDb.OutboxMessages.Add(new OutboxMessage
                {
                    Type = AppointmentIntegrationEventTypes.Created,
                    Payload = "{}",
                    CreatedAtUtc = baseTime.AddMinutes(i)
                });
            }

            var emailMessage = new OutboxMessage
            {
                Type = OutboxMessageTypes.Email,
                Payload = $$"""{"To":"noop@example.com","Subject":"{{subjectMarker}}","Body":"integration"}""",
                CreatedAtUtc = baseTime.AddHours(1)
            };
            commandDb.OutboxMessages.Add(emailMessage);
            await commandDb.SaveChangesAsync();
            emailMessageId = emailMessage.Id;
        }

        OutboxMessage? emailRow = null;
        await using (var pollScope = _factory.Services.CreateAsyncScope())
        {
            var db = pollScope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                emailRow = await db.OutboxMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == emailMessageId);
                if (emailRow?.ProcessedAtUtc is not null)
                    break;
            }
        }

        emailRow.Should().NotBeNull();
        emailRow!.ProcessedAtUtc.Should().NotBeNull("email mesaji appointment mesajlarinin arkasinda kalsa da islenmeli");

        await using (var cleanup = _factory.Services.CreateAsyncScope())
        {
            var db = cleanup.ServiceProvider.GetRequiredService<AppDbContext>();
            var appointmentRows = (await db.OutboxMessages.AsNoTracking().Select(m => m).ToListAsync())
                .Where(m => AppointmentIntegrationEventTypes.IsKnown(m.Type))
                .ToList();
            appointmentRows.Should().AllSatisfy(m =>
            {
                m.ProcessedAtUtc.Should().BeNull();
                m.RetryCount.Should().Be(0);
            });

            db.OutboxMessages.RemoveRange(await db.OutboxMessages.ToListAsync());
            await db.SaveChangesAsync();
        }
    }
}
