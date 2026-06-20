using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Pets;

[CollectionDefinition("pet-projection", DisableParallelization = true)]
public sealed class PetProjectionCollection : ICollectionFixture<PetProjectionWebApplicationFactory>;

[Collection("pet-projection")]
public sealed class PetProjectionIntegrationTests
{
    private readonly PetProjectionWebApplicationFactory _factory;

    public PetProjectionIntegrationTests(PetProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task CreatedEvent_Should_InsertReadModel_And_MarkOutboxProcessed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var petId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = PetProjectionTestSupport.CreateSnapshot(petId, tenantId, clientId, name: "Pamuk");

        var integrationEvent = new PetCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot);
        var outbox = await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PetIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        var readModel = await queryDb.PetReadModels.SingleAsync(x => x.PetId == petId);
        readModel.TenantId.Should().Be(tenantId);
        readModel.ClientId.Should().Be(clientId);
        readModel.Name.Should().Be("Pamuk");
        readModel.SpeciesName.Should().Be("Kedi");
        readModel.LastEventId.Should().Be(eventId);
        readModel.LastEventOccurredAtUtc.Should().Be(occurredAtUtc);
        readModel.LastProjectedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        (await queryDb.ProcessedProjectionEvents.CountAsync(
            x => x.EventId == eventId && x.ConsumerName == PetProjectionTestSupport.ConsumerName))
            .Should().Be(1);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdatedEvent_Should_UpdateExistingReadModelRow()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var petId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 20, 9, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc);

        var created = PetProjectionTestSupport.CreateSnapshot(
            petId, tenantId, clientId, name: "Eski", speciesId: speciesId, weight: 3m);
        var updated = PetProjectionTestSupport.CreateSnapshot(
            petId, tenantId, clientId, name: "Pamuk", speciesId: speciesId, weight: 5.5m);

        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Created,
            new PetCreatedIntegrationEvent(Guid.NewGuid(), createdAt, created));
        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Updated,
            new PetUpdatedIntegrationEvent(Guid.NewGuid(), updatedAt, updated));

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.PetReadModels.CountAsync(x => x.PetId == petId)).Should().Be(1);
        var readModel = await queryDb.PetReadModels.SingleAsync(x => x.PetId == petId);
        readModel.Name.Should().Be("Pamuk");
        readModel.Weight.Should().Be(5.5m);
        readModel.LastEventOccurredAtUtc.Should().Be(updatedAt);
    }

    [Fact]
    public async Task DuplicateEvent_Should_BeIdempotent()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var petId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = PetProjectionTestSupport.CreateSnapshot(petId, tenantId, clientId);
        var integrationEvent = new PetCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot);

        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PetIntegrationEventTypes.Created, integrationEvent);
        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PetIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.PetReadModels.CountAsync(x => x.PetId == petId)).Should().Be(1);
        (await queryDb.ProcessedProjectionEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);

        var outboxRows = await commandDb.OutboxMessages.AsNoTracking().ToListAsync();
        outboxRows.Should().OnlyContain(x => x.ProcessedAtUtc != null);
        outboxRows.Should().OnlyContain(x => x.DeadLetterAtUtc == null);
    }

    [Fact]
    public async Task StaleEvent_Should_NotOverwriteNewerReadModelData()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var petId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var newerAt = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        var olderAt = new DateTime(2026, 6, 20, 8, 0, 0, DateTimeKind.Utc);

        var newerSnap = PetProjectionTestSupport.CreateSnapshot(
            petId, tenantId, clientId, name: "Newer", weight: 10m);
        var olderSnap = PetProjectionTestSupport.CreateSnapshot(
            petId, tenantId, clientId, name: "Older", weight: 1m);

        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Updated,
            new PetUpdatedIntegrationEvent(Guid.NewGuid(), newerAt, newerSnap));
        var staleOutbox = await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Updated,
            new PetUpdatedIntegrationEvent(Guid.NewGuid(), olderAt, olderSnap));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.PetReadModels.SingleAsync(x => x.PetId == petId);
        readModel.Name.Should().Be("Newer", "daha eski OccurredAtUtc taşıyan event yeni veriyi ezmemeli");
        readModel.Weight.Should().Be(10m);
        readModel.LastEventOccurredAtUtc.Should().Be(newerAt);

        await commandDb.Entry(staleOutbox).ReloadAsync();
        staleOutbox.ProcessedAtUtc.Should().NotBeNull();
        staleOutbox.DeadLetterAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task TenantIsolation_Should_KeepSeparateReadModels()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var petA = Guid.NewGuid();
        var petB = Guid.NewGuid();

        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Created,
            new PetCreatedIntegrationEvent(
                Guid.NewGuid(),
                new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),
                PetProjectionTestSupport.CreateSnapshot(petA, tenantA, Guid.NewGuid(), name: "PetA")));
        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Created,
            new PetCreatedIntegrationEvent(
                Guid.NewGuid(),
                new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),
                PetProjectionTestSupport.CreateSnapshot(petB, tenantB, Guid.NewGuid(), name: "PetB")));

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.PetReadModels.CountAsync()).Should().Be(2);
        (await queryDb.PetReadModels.CountAsync(x => x.TenantId == tenantA && x.Name == "PetA")).Should().Be(1);
        (await queryDb.PetReadModels.CountAsync(x => x.TenantId == tenantB && x.Name == "PetB")).Should().Be(1);
    }

    [Fact]
    public async Task UnknownPetEventType_Should_NotBeClaimed_And_RemainUntouched()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var unknown = await PetProjectionTestSupport.EnqueueRawAsync(
            commandDb, "pet.unknown.v1", """{"petId":"x"}""");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(unknown).ReloadAsync();
        unknown.ProcessedAtUtc.Should().BeNull();
        unknown.DeadLetterAtUtc.Should().BeNull();
        unknown.RetryCount.Should().Be(0);
        (await queryDb.PetReadModels.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvalidJson_Should_Retry_And_NotMarkOutboxProcessed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var outbox = await PetProjectionTestSupport.EnqueueRawAsync(
            commandDb, PetIntegrationEventTypes.Created, "{ invalid json");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        (await queryDb.PetReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().BeNull();
        outbox.RetryCount.Should().Be(1);
        outbox.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NonPetOutboxMessage_Should_NotBeConsumedByPetProcessor()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var email = await PetProjectionTestSupport.EnqueueRawAsync(
            commandDb,
            OutboxMessageTypes.Email,
            """{"To":"test@example.com","Subject":"x","Body":"y"}""");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(email).ReloadAsync();
        email.ProcessedAtUtc.Should().BeNull();
    }
}
