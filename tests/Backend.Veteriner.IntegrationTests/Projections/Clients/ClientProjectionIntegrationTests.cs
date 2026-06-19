using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Outbox;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Clients;

[CollectionDefinition("client-projection", DisableParallelization = true)]
public sealed class ClientProjectionCollection : ICollectionFixture<ClientProjectionWebApplicationFactory>;

[Collection("client-projection")]
public sealed class ClientProjectionIntegrationTests
{
    private readonly ClientProjectionWebApplicationFactory _factory;

    public ClientProjectionIntegrationTests(ClientProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task CreatedEvent_Should_InsertReadModel_And_MarkOutboxProcessed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId, "Mehmet Demir");

        var integrationEvent = new ClientCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot);
        var outbox = await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, ClientIntegrationEventTypes.Created, integrationEvent);

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(1);

        var readModel = await queryDb.ClientReadModels.SingleAsync(x => x.ClientId == clientId);
        readModel.TenantId.Should().Be(tenantId);
        readModel.FullName.Should().Be("Mehmet Demir");
        readModel.LastEventId.Should().Be(eventId);
        readModel.LastEventOccurredAtUtc.Should().Be(occurredAtUtc);

        (await queryDb.ProcessedProjectionEvents.CountAsync(
            x => x.EventId == eventId && x.ConsumerName == ClientProjectionTestSupport.ConsumerName))
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
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 6, 19, 9, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 6, 19, 11, 0, 0, DateTimeKind.Utc);

        var created = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId, "İlk İsim", email: "ilk@example.com");
        var updated = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId, "Yeni İsim", email: "yeni@example.com");

        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            ClientIntegrationEventTypes.Created,
            new ClientCreatedIntegrationEvent(Guid.NewGuid(), createdAt, created));
        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            ClientIntegrationEventTypes.Updated,
            new ClientUpdatedIntegrationEvent(Guid.NewGuid(), updatedAt, updated));

        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.ClientReadModels.CountAsync(x => x.ClientId == clientId)).Should().Be(1);
        var readModel = await queryDb.ClientReadModels.SingleAsync(x => x.ClientId == clientId);
        readModel.FullName.Should().Be("Yeni İsim");
        readModel.Email.Should().Be("yeni@example.com");
        readModel.LastEventOccurredAtUtc.Should().Be(updatedAt);
    }

    [Fact]
    public async Task DuplicateEvent_Should_BeIdempotent()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAtUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);
        var snapshot = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId);
        var integrationEvent = new ClientCreatedIntegrationEvent(eventId, occurredAtUtc, snapshot);

        // Aynı EventId iki ayrı outbox satırı olarak enqueue edilir.
        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, ClientIntegrationEventTypes.Created, integrationEvent);
        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb, ClientIntegrationEventTypes.Created, integrationEvent);

        await processor.ProcessBatchAsync(CancellationToken.None);
        await processor.ProcessBatchAsync(CancellationToken.None);

        (await queryDb.ClientReadModels.CountAsync(x => x.ClientId == clientId)).Should().Be(1);
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
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var clientId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var newerAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);
        var olderAt = new DateTime(2026, 6, 19, 8, 0, 0, DateTimeKind.Utc);

        // Newer update önce işlenir (outbox sırası newer → older; out-of-order arrival).
        var newerSnap = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId, "Newer", email: "newer@example.com");
        var olderSnap = ClientProjectionTestSupport.CreateSnapshot(clientId, tenantId, "Older", email: "older@example.com");

        await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            ClientIntegrationEventTypes.Updated,
            new ClientUpdatedIntegrationEvent(Guid.NewGuid(), newerAt, newerSnap));
        var staleOutbox = await ClientProjectionTestSupport.EnqueueIntegrationEventAsync(
            commandDb,
            ClientIntegrationEventTypes.Updated,
            new ClientUpdatedIntegrationEvent(Guid.NewGuid(), olderAt, olderSnap));

        await processor.ProcessBatchAsync(CancellationToken.None);

        var readModel = await queryDb.ClientReadModels.SingleAsync(x => x.ClientId == clientId);
        readModel.FullName.Should().Be("Newer", "daha eski OccurredAtUtc taşıyan event yeni veriyi ezmemeli");
        readModel.Email.Should().Be("newer@example.com");
        readModel.LastEventOccurredAtUtc.Should().Be(newerAt);

        // Stale event yine de tüketilir (dedup yazılır, outbox processed).
        await commandDb.Entry(staleOutbox).ReloadAsync();
        staleOutbox.ProcessedAtUtc.Should().NotBeNull();
        staleOutbox.DeadLetterAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task UnknownClientEventType_Should_NotBeClaimed_And_RemainUntouched()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        // Bilinmeyen client-benzeri tip: processor yalnızca bilinen client projection tiplerini claim eder.
        var unknown = await ClientProjectionTestSupport.EnqueueRawAsync(
            commandDb, "client.unknown.v1", """{"clientId":"x"}""");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(unknown).ReloadAsync();
        unknown.ProcessedAtUtc.Should().BeNull();
        unknown.DeadLetterAtUtc.Should().BeNull();
        unknown.RetryCount.Should().Be(0);
        (await queryDb.ClientReadModels.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InvalidJson_Should_Retry_And_NotMarkOutboxProcessed()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var outbox = await ClientProjectionTestSupport.EnqueueRawAsync(
            commandDb, ClientIntegrationEventTypes.Created, "{ invalid json");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        (await queryDb.ClientReadModels.CountAsync()).Should().Be(0);
        (await queryDb.ProcessedProjectionEvents.CountAsync()).Should().Be(0);

        await commandDb.Entry(outbox).ReloadAsync();
        outbox.ProcessedAtUtc.Should().BeNull();
        outbox.RetryCount.Should().Be(1);
        outbox.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NonClientOutboxMessage_Should_NotBeConsumedByClientProcessor()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClientProjectionProcessor>();

        await ClientProjectionTestSupport.ResetQuerySideAsync(queryDb);
        await ClientProjectionTestSupport.ClearOutboxAsync(commandDb);

        var email = await ClientProjectionTestSupport.EnqueueRawAsync(
            commandDb,
            OutboxMessageTypes.Email,
            """{"To":"test@example.com","Subject":"x","Body":"y"}""");

        var processed = await processor.ProcessBatchAsync(CancellationToken.None);
        processed.Should().Be(0);

        await commandDb.Entry(email).ReloadAsync();
        email.ProcessedAtUtc.Should().BeNull();
    }
}
