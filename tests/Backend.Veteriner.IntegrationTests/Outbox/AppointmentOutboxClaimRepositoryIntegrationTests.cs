using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Entities;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Outbox;

[Collection("appointment-projection")]
public sealed class AppointmentOutboxClaimRepositoryIntegrationTests : IClassFixture<AppointmentProjectionWebApplicationFactory>
{
    private static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(60);

    private readonly AppointmentProjectionWebApplicationFactory _factory;

    public AppointmentOutboxClaimRepositoryIntegrationTests(AppointmentProjectionWebApplicationFactory factory)
        => _factory = factory;

    [Fact]
    public async Task ClaimNextBatch_TwoWorkers_Should_NotClaimSameRow()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        await SeedMessageAsync(appointmentId, sequence: 1);

        var workerA = "worker-a:1:aaaa";
        var workerB = "worker-b:2:bbbb";

        await using var scopeA = _factory.Services.CreateAsyncScope();
        await using var scopeB = _factory.Services.CreateAsyncScope();
        var repoA = scopeA.ServiceProvider.GetRequiredService<IAppointmentOutboxClaimRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<IAppointmentOutboxClaimRepository>();

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
        var appointmentId = Guid.NewGuid();
        var message = await SeedMessageAsync(appointmentId, sequence: 1);
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
        var appointmentId = Guid.NewGuid();
        var message = await SeedMessageAsync(appointmentId, sequence: 1);

        await ClaimSingleAsync("worker-owner:1:owner1", message.Id);

        var secondClaim = await ClaimSingleOrDefaultAsync("worker-other:2:other2");
        secondClaim.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextBatch_ExpiredLease_Should_ReclaimByAnotherWorker()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        var message = await SeedMessageAsync(appointmentId, sequence: 1);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.OutboxMessages.SingleAsync(m => m.Id == message.Id);
            row.ClaimedBy = "stale-worker:1:stale01";
            row.ClaimToken = Guid.NewGuid();
            row.ClaimedAtUtc = DateTime.UtcNow.AddMinutes(-5);
            row.LeaseExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        const string reclaimer = "worker-reclaim:3:reclaim";
        var claimed = await ClaimSingleAsync(reclaimer, message.Id);

        claimed.ClaimedBy.Should().Be(reclaimer);
        claimed.ClaimToken.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MarkProcessed_StaleToken_Should_ReturnFalse()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        var message = await SeedMessageAsync(appointmentId, sequence: 1);
        var claimed = await ClaimSingleAsync("worker-ok:1:ok0001", message.Id);

        var result = await MarkProcessedAsync(message.Id, Guid.NewGuid(), claimed.ClaimedBy);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkProcessed_ValidToken_Should_ReturnTrueAndSetProcessedAtUtc()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        var message = await SeedMessageAsync(appointmentId, sequence: 1);
        var claimed = await ClaimSingleAsync("worker-ok:2:ok0002", message.Id);

        var result = await MarkProcessedAsync(message.Id, claimed.ClaimToken, claimed.ClaimedBy);
        result.Should().BeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == message.Id);

        row.ProcessedAtUtc.Should().NotBeNull();
        row.ClaimToken.Should().BeNull();
        row.ClaimedBy.Should().BeNull();
    }

    [Fact]
    public async Task MarkRetry_StaleToken_Should_ReturnFalse()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        var message = await SeedMessageAsync(appointmentId, sequence: 1);
        var claimed = await ClaimSingleAsync("worker-retry:1:retry01", message.Id);

        var result = await MarkRetryAsync(
            message.Id,
            Guid.NewGuid(),
            claimed.ClaimedBy,
            retryCount: 1,
            DateTime.UtcNow.AddMinutes(1),
            "stale");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkDeadLetter_StaleToken_Should_ReturnFalse()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        var message = await SeedMessageAsync(appointmentId, sequence: 1);
        var claimed = await ClaimSingleAsync("worker-dl:1:dead0001", message.Id);

        var result = await MarkDeadLetterAsync(message.Id, Guid.NewGuid(), claimed.ClaimedBy, "stale");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ClaimNextBatch_SequenceTwoPendingWhileSequenceOnePending_Should_NotClaimSequenceTwo()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        await SeedMessageAsync(appointmentId, sequence: 1);
        await SeedMessageAsync(appointmentId, sequence: 2, createdAtUtc: DateTime.UtcNow.AddSeconds(1));

        var claimed = await ClaimBatchAsync("worker-hol:1:hol0001", batchSize: 5);

        claimed.Should().HaveCount(1);
        claimed[0].AppointmentSequence.Should().Be(1);
    }

    [Fact]
    public async Task ClaimNextBatch_AfterSequenceOneProcessed_Should_ClaimSequenceTwo()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        var first = await SeedMessageAsync(appointmentId, sequence: 1);
        await SeedMessageAsync(appointmentId, sequence: 2, createdAtUtc: DateTime.UtcNow.AddSeconds(1));

        var firstClaim = await ClaimSingleAsync("worker-seq:1:seq0001", first.Id);
        (await MarkProcessedAsync(first.Id, firstClaim.ClaimToken, firstClaim.ClaimedBy)).Should().BeTrue();

        var secondClaim = await ClaimSingleOrDefaultAsync("worker-seq:2:seq0002");
        secondClaim.Should().NotBeNull();
        secondClaim!.AppointmentSequence.Should().Be(2);
    }

    [Fact]
    public async Task ClaimNextBatch_LowerSequenceRetryWaiting_Should_BlockHigherSequence()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        await SeedMessageAsync(
            appointmentId,
            sequence: 1,
            nextAttemptAtUtc: DateTime.UtcNow.AddMinutes(10));
        await SeedMessageAsync(appointmentId, sequence: 2, createdAtUtc: DateTime.UtcNow.AddSeconds(1));

        var claimed = await ClaimBatchAsync("worker-retry-hol:1:retryhol", batchSize: 5);
        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimNextBatch_LowerSequenceDeadLetter_Should_BlockHigherSequence()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        await SeedMessageAsync(appointmentId, sequence: 1, deadLetterAtUtc: DateTime.UtcNow);
        await SeedMessageAsync(appointmentId, sequence: 2, createdAtUtc: DateTime.UtcNow.AddSeconds(1));

        var claimed = await ClaimBatchAsync("worker-dl-hol:1:dlhol01", batchSize: 5);
        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimNextBatch_DifferentAppointments_Should_ClaimBothInSameBatch()
    {
        await ResetOutboxAsync();
        var appointmentA = Guid.NewGuid();
        var appointmentB = Guid.NewGuid();
        await SeedMessageAsync(appointmentA, sequence: 1, createdAtUtc: DateTime.UtcNow);
        await SeedMessageAsync(appointmentB, sequence: 1, createdAtUtc: DateTime.UtcNow.AddMilliseconds(1));

        var claimed = await ClaimBatchAsync("worker-multi:1:multi01", batchSize: 2);

        claimed.Should().HaveCount(2);
        claimed.Select(c => c.AppointmentId).Should().BeEquivalentTo([appointmentA, appointmentB]);
    }

    [Fact]
    public async Task ClaimNextBatch_NullAppointmentMetadata_Should_NotClaim()
    {
        await ResetOutboxAsync();
        await SeedMessageAsync(appointmentId: null, sequence: null);
        await SeedMessageAsync(appointmentId: Guid.NewGuid(), sequence: null);

        var claimed = await ClaimBatchAsync("worker-null:1:null001", batchSize: 5);
        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimNextBatch_NonAppointmentType_Should_NotClaim()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Type = "email",
                Payload = "{}",
                AppointmentId = appointmentId,
                AppointmentSequence = 1
            });
            await db.SaveChangesAsync();
        }

        var claimed = await ClaimBatchAsync("worker-nonap:1:nonap01", batchSize: 5);
        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimNextBatch_Ordering_Should_BeDeterministicByCreatedAtSequenceAndId()
    {
        await ResetOutboxAsync();
        var appointmentA = Guid.NewGuid();
        var appointmentB = Guid.NewGuid();
        var earlier = DateTime.UtcNow.AddMinutes(-10);
        var later = DateTime.UtcNow.AddMinutes(-5);

        var msgB = await SeedMessageAsync(appointmentB, sequence: 1, createdAtUtc: later);
        var msgA = await SeedMessageAsync(appointmentA, sequence: 1, createdAtUtc: earlier);

        var claimed = await ClaimBatchAsync("worker-order:1:order01", batchSize: 2);

        claimed.Should().HaveCount(2);
        claimed[0].Id.Should().Be(msgA.Id);
        claimed[1].Id.Should().Be(msgB.Id);
    }

    [Fact]
    public async Task ClaimNextBatch_DefaultBatchSizeOne_Should_ReturnSingleRow()
    {
        await ResetOutboxAsync();
        var appointmentA = Guid.NewGuid();
        var appointmentB = Guid.NewGuid();
        await SeedMessageAsync(appointmentA, sequence: 1);
        await SeedMessageAsync(appointmentB, sequence: 1, createdAtUtc: DateTime.UtcNow.AddSeconds(1));

        var claimed = await ClaimBatchAsync("worker-batch1:1:batch01", batchSize: 1);
        claimed.Should().HaveCount(1);
    }

    [Fact]
    public async Task ClaimNextBatch_BatchSizeGreaterThanOne_Should_NotClaimSameAppointmentSequencesTogether()
    {
        await ResetOutboxAsync();
        var appointmentId = Guid.NewGuid();
        await SeedMessageAsync(appointmentId, sequence: 1);
        await SeedMessageAsync(appointmentId, sequence: 2, createdAtUtc: DateTime.UtcNow.AddSeconds(1));

        var claimed = await ClaimBatchAsync("worker-batchn:1:batchn01", batchSize: 2);

        claimed.Should().HaveCount(1);
        claimed[0].AppointmentSequence.Should().Be(1);
    }

    [Fact]
    public async Task WorkerIdentity_Should_BeStableWithinProcessAndWithinMaxLength()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionWorkerIdentity>();

        identity.WorkerId.Should().NotBeNullOrWhiteSpace();
        identity.WorkerId.Length.Should().BeLessThanOrEqualTo(128);
        identity.WorkerId.Should().Contain(":");

        var sameScopeIdentity = scope.ServiceProvider.GetRequiredService<IAppointmentProjectionWorkerIdentity>();
        sameScopeIdentity.WorkerId.Should().Be(identity.WorkerId);
    }

    private async Task ResetOutboxAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.OutboxMessages.ExecuteDeleteAsync();
    }

    private async Task<OutboxMessage> SeedMessageAsync(
        Guid? appointmentId,
        long? sequence,
        DateTime? createdAtUtc = null,
        DateTime? nextAttemptAtUtc = null,
        DateTime? deadLetterAtUtc = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var message = new OutboxMessage
        {
            Type = AppointmentIntegrationEventTypes.Created,
            Payload = """{"appointmentId":"00000000-0000-0000-0000-000000000001"}""",
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            AppointmentId = appointmentId,
            AppointmentSequence = sequence,
            NextAttemptAtUtc = nextAttemptAtUtc,
            DeadLetterAtUtc = deadLetterAtUtc
        };

        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    private async Task<ClaimedAppointmentOutboxMessage> ClaimSingleAsync(string workerId, Guid expectedMessageId)
    {
        var claimed = await ClaimSingleOrDefaultAsync(workerId);
        claimed.Should().NotBeNull();
        claimed!.Id.Should().Be(expectedMessageId);
        return claimed;
    }

    private async Task<ClaimedAppointmentOutboxMessage?> ClaimSingleOrDefaultAsync(string workerId)
    {
        var batch = await ClaimBatchAsync(workerId, batchSize: 1);
        return batch.Count == 0 ? null : batch[0];
    }

    private async Task<IReadOnlyList<ClaimedAppointmentOutboxMessage>> ClaimBatchAsync(string workerId, int batchSize)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppointmentOutboxClaimRepository>();
        return await repo.ClaimNextBatchAsync(workerId, batchSize, DefaultLease, CancellationToken.None);
    }

    private async Task<bool> MarkProcessedAsync(Guid messageId, Guid claimToken, string workerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppointmentOutboxClaimRepository>();
        return await repo.MarkProcessedAsync(messageId, claimToken, workerId, CancellationToken.None);
    }

    private async Task<bool> MarkRetryAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        int retryCount,
        DateTime nextAttemptAtUtc,
        string error)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppointmentOutboxClaimRepository>();
        return await repo.MarkRetryAsync(
            messageId,
            claimToken,
            workerId,
            retryCount,
            nextAttemptAtUtc,
            error,
            CancellationToken.None);
    }

    private async Task<bool> MarkDeadLetterAsync(Guid messageId, Guid claimToken, string workerId, string error)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppointmentOutboxClaimRepository>();
        return await repo.MarkDeadLetterAsync(messageId, claimToken, workerId, error, CancellationToken.None);
    }
}
