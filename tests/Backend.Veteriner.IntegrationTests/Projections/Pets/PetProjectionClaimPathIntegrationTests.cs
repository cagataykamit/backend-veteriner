using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Pets;

[Collection("pet-projection-claim")]
public sealed class PetProjectionClaimPathIntegrationTests
{
    private readonly PetProjectionClaimEnabledWebApplicationFactory _claimFactory;

    public PetProjectionClaimPathIntegrationTests(PetProjectionClaimEnabledWebApplicationFactory claimFactory)
        => _claimFactory = claimFactory;

    [Fact]
    public async Task ClaimingEnabled_Should_MarkProcessedWithClaimToken()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var petId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var snapshot = PetProjectionTestSupport.CreateSnapshot(petId, tenantId, clientId, name: "Pamuk");
        var integrationEvent = new PetCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);

        var outbox = await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PetIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
        outbox.ClaimedBy.Should().BeNull();
        (await queryDb.PetReadModels.CountAsync(x => x.PetId == petId)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_DuplicateEvent_Should_KeepReadModelIdempotent()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var petId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var snapshot = PetProjectionTestSupport.CreateSnapshot(petId, tenantId, clientId);
        var integrationEvent = new PetCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);

        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PetIntegrationEventTypes.Created, integrationEvent);
        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, PetIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.PetReadModels.CountAsync()).Should().Be(1);
        (await queryDb.ProcessedProjectionEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_StaleEvent_Should_NotOverwriteNewerReadModelData()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
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

        var newerSnap = PetProjectionTestSupport.CreateSnapshot(petId, tenantId, clientId, name: "Newer", weight: 10m);
        var olderSnap = PetProjectionTestSupport.CreateSnapshot(petId, tenantId, clientId, name: "Older", weight: 1m);

        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Updated,
            new PetUpdatedIntegrationEvent(Guid.NewGuid(), newerAt, newerSnap));
        await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            PetIntegrationEventTypes.Updated,
            new PetUpdatedIntegrationEvent(Guid.NewGuid(), olderAt, olderSnap));

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.PetReadModels.SingleAsync(x => x.PetId == petId);
        readModel.Name.Should().Be("Newer");
        readModel.Weight.Should().Be(10m);
        readModel.LastEventOccurredAtUtc.Should().Be(newerAt);
    }

    [Fact]
    public async Task ClaimPath_ApplyException_Should_MarkRetryWithClaimToken()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IPetProjectionProcessor>();

        await PetProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await PetProjectionTestSupport.ClearOutboxAsync(commandDb);

        var outbox = await PetProjectionTestSupport.EnqueueRawAsync(
            commandDb, PetIntegrationEventTypes.Created, "{ invalid json");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.RetryCount.Should().Be(1);
        outbox.NextAttemptAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
        outbox.ProcessedAtUtc.Should().BeNull();
    }
}

[CollectionDefinition("pet-projection-claim", DisableParallelization = true)]
public sealed class PetProjectionClaimCollection : ICollectionFixture<PetProjectionClaimEnabledWebApplicationFactory>;
