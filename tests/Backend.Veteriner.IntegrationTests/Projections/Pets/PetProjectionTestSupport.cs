using System.Text.Json;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.IntegrationTests.Projections.Pets;

internal static class PetProjectionTestSupport
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string ConsumerName = "pet-read-model-v1";

    public static async Task ResetQuerySideAsync(QueryDbContext queryDb, CancellationToken ct = default)
        => await IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb, ct);

    public static async Task ClearOutboxAsync(AppDbContext commandDb, CancellationToken ct = default)
        => await commandDb.OutboxMessages.ExecuteDeleteAsync(ct);

    public static PetProjectionSnapshot CreateSnapshot(
        Guid petId,
        Guid tenantId,
        Guid clientId,
        string name = "Pamuk",
        string clientFullName = "Ayşe Yılmaz",
        Guid? speciesId = null,
        string speciesName = "Kedi",
        string? breed = "Tekir",
        string? breedRefName = null,
        Guid? breedId = null,
        Guid? colorId = null,
        string? colorName = "Siyah",
        int? gender = 2,
        DateOnly? birthDate = null,
        decimal? weight = 4.25m)
    {
        speciesId ??= Guid.NewGuid();
        return new PetProjectionSnapshot(
            petId,
            tenantId,
            clientId,
            clientFullName,
            Client.NormalizeFullNameForDuplicateCheck(clientFullName),
            name,
            name.Trim().ToLowerInvariant(),
            speciesId.Value,
            speciesName,
            speciesName.Trim().ToLowerInvariant(),
            breedId,
            breed,
            breedRefName,
            colorId,
            colorName,
            string.IsNullOrWhiteSpace(colorName) ? null : colorName.Trim().ToLowerInvariant(),
            gender,
            birthDate ?? new DateOnly(2024, 3, 10),
            weight);
    }

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
}
