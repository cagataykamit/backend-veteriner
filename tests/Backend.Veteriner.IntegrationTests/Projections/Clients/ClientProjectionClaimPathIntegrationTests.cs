using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Clients;

[Collection("client-projection-claim")]
public sealed class ClientProjectionClaimPathIntegrationTests
{
    private readonly ClientProjectionClaimEnabledWebApplicationFactory _claimFactory;
    private readonly ClientProjectionClaimInstrumentedWebApplicationFactory _instrumentedFactory;

    public ClientProjectionClaimPathIntegrationTests(
        ClientProjectionClaimEnabledWebApplicationFactory claimFactory,
        ClientProjectionClaimInstrumentedWebApplicationFactory instrumentedFactory)
    {
        _claimFactory = claimFactory;
        _instrumentedFactory = instrumentedFactory;
    }

    [Fact]
    public async Task ClaimingEnabled_Should_MarkProcessedWithClaimToken()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var snapshot = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId);
        var integrationEvent = new ClientCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);

        var outbox = await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, ClientIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
        outbox.ClaimedBy.Should().BeNull();
        (await queryDb.ClientReadModels.CountAsync(x => x.ClientId == clientId)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_DuplicateEvent_Should_KeepReadModelIdempotent()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var snapshot = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId);
        var integrationEvent = new ClientCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);

        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, ClientIntegrationEventTypes.Created, integrationEvent);
        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, ClientIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.ClientReadModels.CountAsync()).Should().Be(1);
        (await queryDb.ProcessedProjectionEvents.CountAsync(x => x.EventId == eventId)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_StaleEvent_Should_NotOverwriteNewerReadModelData()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var newerAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var olderAt = new DateTime(2026, 6, 19, 8, 0, 0, DateTimeKind.Utc);

        var newerSnap = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId, "Newer");
        var olderSnap = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId, "Older");

        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            ClientIntegrationEventTypes.Updated,
            new ClientUpdatedIntegrationEvent(Guid.NewGuid(), newerAt, newerSnap));
        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            ClientIntegrationEventTypes.Updated,
            new ClientUpdatedIntegrationEvent(Guid.NewGuid(), olderAt, olderSnap));

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.ClientReadModels.SingleAsync(x => x.ClientId == clientId);
        readModel.FullName.Should().Be("Newer");
        readModel.LastEventOccurredAtUtc.Should().Be(newerAt);
    }

    [Fact]
    public async Task ClaimPath_StaleMarkProcessedFalse_Should_NotCountAsBatchFailure()
    {
        await using var scope = _instrumentedFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();
        var claimRepo = scope.ServiceProvider.GetRequiredService<TestClientOutboxClaimRepository>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var snapshot = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId);
        var integrationEvent = new ClientCreatedIntegrationEvent(eventId, DateTime.UtcNow, snapshot);

        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, ClientIntegrationEventTypes.Created, integrationEvent);

        claimRepo.RejectNextMarkProcessed = true;

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);

        processed.Should().Be(1);
        claimRepo.MarkProcessedCallCount.Should().Be(1);
        (await queryDb.ClientReadModels.CountAsync(x => x.ClientId == clientId)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimPath_ApplyException_Should_MarkRetryWithClaimToken()
    {
        await using var scope = _claimFactory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var outbox = await ClientProjectionTestSupport.EnqueueRawAsync(
            commandDb, ClientIntegrationEventTypes.Created, "{ invalid json");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.RetryCount.Should().Be(1);
        outbox.NextAttemptAtUtc.Should().NotBeNull();
        outbox.ClaimToken.Should().BeNull();
        outbox.ProcessedAtUtc.Should().BeNull();
    }
}

[CollectionDefinition("client-projection-claim", DisableParallelization = true)]
public sealed class ClientProjectionClaimCollection
    : ICollectionFixture<ClientProjectionClaimEnabledWebApplicationFactory>,
      ICollectionFixture<ClientProjectionClaimInstrumentedWebApplicationFactory>;
