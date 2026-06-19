using System.Text.Json;
using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Projections.Clients;

internal static class ClientProjectionTestSupport
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string ConsumerName = "client-read-model-v1";

    public static async Task ResetQuerySideAsync(QueryDbContext queryDb, CancellationToken ct = default)
        => await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb, ct);

    public static async Task ClearOutboxAsync(AppDbContext commandDb, CancellationToken ct = default)
        => await commandDb.OutboxMessages.ExecuteDeleteAsync(ct);

    public static ClientProjectionSnapshot CreateSnapshot(
        Guid clientId,
        Guid tenantId,
        string fullName = "Ayşe Yılmaz",
        string? email = "ayse@example.com",
        string? phone = "905321234567",
        DateTime? createdAtUtc = null)
        => new(
            clientId,
            tenantId,
            fullName,
            fullName.Trim().ToLowerInvariant(),
            email,
            phone,
            phone,
            createdAtUtc ?? new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc));

    public static async Task<OutboxMessage> EnqueueIntegrationEventAsync(
        AppDbContext commandDb,
        string eventType,
        object integrationEvent,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Json);
        var message = new OutboxMessage
        {
            Type = eventType,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow,
            AppointmentId = null,
            AppointmentSequence = null
        };

        commandDb.OutboxMessages.Add(message);
        await commandDb.SaveChangesAsync(ct);
        return message;
    }

    public static async Task<OutboxMessage> EnqueueRawAsync(
        AppDbContext commandDb,
        string eventType,
        string payload,
        CancellationToken ct = default)
    {
        var message = new OutboxMessage
        {
            Type = eventType,
            Payload = payload,
            CreatedAtUtc = DateTime.UtcNow
        };

        commandDb.OutboxMessages.Add(message);
        await commandDb.SaveChangesAsync(ct);
        return message;
    }

    public static async Task MarkProcessedInQueryDbAsync(
        QueryDbContext queryDb,
        Guid eventId,
        CancellationToken ct = default)
    {
        queryDb.ProcessedProjectionEvents.Add(new ProcessedProjectionEvent
        {
            EventId = eventId,
            ConsumerName = ConsumerName,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await queryDb.SaveChangesAsync(ct);
    }
}
