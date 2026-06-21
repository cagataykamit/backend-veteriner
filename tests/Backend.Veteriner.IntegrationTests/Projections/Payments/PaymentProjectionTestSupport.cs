using System.Text.Json;
using Backend.Veteriner.Application.Payments.IntegrationEvents;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Backend.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Projections.Payments;

internal static class PaymentProjectionTestSupport
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string ConsumerName = "payment-finance-v1";

    public static async Task ResetQuerySideAsync(QueryDbContext queryDb, CancellationToken ct = default)
        => await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb, ct);

    public static async Task ClearOutboxAsync(AppDbContext commandDb, CancellationToken ct = default)
        => await commandDb.OutboxMessages.ExecuteDeleteAsync(ct);

    public static PaymentProjectionSnapshot CreateSnapshot(
        Guid paymentId,
        Guid tenantId,
        Guid clinicId,
        decimal amount = 100m,
        string currency = "TRY",
        DateTime? paidAtUtc = null,
        Guid? clientId = null)
        => new(
            paymentId,
            tenantId,
            clinicId,
            clientId ?? Guid.NewGuid(),
            null,
            null,
            null,
            amount,
            currency,
            1,
            paidAtUtc ?? new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc),
            PaymentIntegrationEventTypes.SchemaVersion);

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
            CreatedAtUtc = DateTime.UtcNow
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

    public static async Task<ClinicDailyPaymentStatsReadModel?> FindDailyStatsAsync(
        QueryDbContext queryDb,
        Guid tenantId,
        Guid clinicId,
        DateOnly localDate,
        string currency,
        CancellationToken ct = default)
        => await queryDb.ClinicDailyPaymentStatsReadModels
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId
                  && x.ClinicId == clinicId
                  && x.LocalDate == localDate
                  && x.Currency == currency,
                ct);
}
