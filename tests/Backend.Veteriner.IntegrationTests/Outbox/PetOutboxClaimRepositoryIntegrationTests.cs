using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Projections;
using Backend.Veteriner.Application.Projections.Pets;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using Backend.IntegrationTests.Projections.Pets;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Outbox;

[Collection("pet-projection-claim")]
public sealed class PetOutboxClaimRepositoryIntegrationTests
    : IClassFixture<PetProjectionClaimEnabledWebApplicationFactory>
{
    private static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(60);

    private readonly PetProjectionClaimEnabledWebApplicationFactory _factory;

    public PetOutboxClaimRepositoryIntegrationTests(PetProjectionClaimEnabledWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ClaimNextBatch_TwoWorkers_Should_NotClaimSameRow()
    {
        await ResetOutboxAsync();
        var message = await SeedMessageAsync();

        const string workerA = "worker-a:1:aaaa";
        const string workerB = "worker-b:2:bbbb";

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<IPetOutboxClaimRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<IPetOutboxClaimRepository>();

        var claimTasks = new[]
        {
            repoA.ClaimNextBatchAsync(workerA, batchSize: 1, DefaultLease, CancellationToken.None),
            repoB.ClaimNextBatchAsync(workerB, batchSize: 1, DefaultLease, CancellationToken.None)
        };

        var results = await Task.WhenAll(claimTasks);
        var claimed = results.SelectMany(r => r).ToList();

        claimed.Should().HaveCount(1);
        claimed[0].ClaimedBy.Should().BeOneOf(workerA, workerB);
    }

    [Fact]
    public async Task ClaimNextBatch_Should_PopulateClaimColumns()
    {
        await ResetOutboxAsync();
        var message = await SeedMessageAsync();
        const string workerId = "worker-claim:99:test01";

        var claimed = await ClaimSingleAsync(workerId, message.Id);

        claimed.ClaimToken.Should().NotBe(Guid.Empty);
        claimed.ClaimedBy.Should().Be(workerId);
        claimed.ClaimedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        claimed.LeaseExpiresAtUtc.Should().BeAfter(claimed.ClaimedAtUtc);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);

        row.ClaimToken.Should().Be(claimed.ClaimToken);
        row.ClaimedBy.Should().Be(workerId);
        row.ClaimedAtUtc.Should().NotBeNull();
        row.LeaseExpiresAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ClaimNextBatch_ActiveLease_Should_NotReclaim()
    {
        await ResetOutboxAsync();
        var message = await SeedMessageAsync();

        await ClaimSingleAsync("worker-owner:1:owner1", message.Id);

        var secondClaim = await ClaimSingleOrDefaultAsync("worker-other:2:other2");
        secondClaim.Should().BeNull();
    }

    private async Task ResetOutboxAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await PetProjectionTestSupport.ClearOutboxAsync(db);
    }

    private async Task<OutboxMessage> SeedMessageAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var petId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var snapshot = PetProjectionTestSupport.CreateSnapshot(petId, tenantId, clientId);
        var integrationEvent = new PetCreatedIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, snapshot);

        return await PetProjectionTestSupport.EnqueueIntegrationEventAsync(
            db, PetIntegrationEventTypes.Created, integrationEvent);
    }

    private async Task<ClaimedIntegrationOutboxMessage> ClaimSingleAsync(string workerId, Guid messageId)
    {
        var claimed = await ClaimSingleOrDefaultAsync(workerId);
        claimed.Should().NotBeNull();
        claimed!.Id.Should().Be(messageId);
        return claimed;
    }

    private async Task<ClaimedIntegrationOutboxMessage?> ClaimSingleOrDefaultAsync(string workerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPetOutboxClaimRepository>();
        var batch = await repo.ClaimNextBatchAsync(workerId, batchSize: 1, DefaultLease, CancellationToken.None);
        return batch.SingleOrDefault();
    }
}
