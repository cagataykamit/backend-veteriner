using Backend.Veteriner.Application.Projections;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Clients;

internal sealed class TestClientOutboxClaimRepository : IClientOutboxClaimRepository
{
    private readonly IServiceProvider _serviceProvider;
    private IClientOutboxClaimRepository? _inner;

    public TestClientOutboxClaimRepository(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public bool RejectNextMarkProcessed { get; set; }

    public int MarkProcessedCallCount { get; private set; }

    private IClientOutboxClaimRepository Inner
        => _inner ??= ActivatorUtilities.CreateInstance<SqlClientOutboxClaimRepository>(_serviceProvider);

    public Task<IReadOnlyList<ClaimedIntegrationOutboxMessage>> ClaimNextBatchAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
        => Inner.ClaimNextBatchAsync(workerId, batchSize, leaseDuration, cancellationToken);

    public async Task<bool> MarkProcessedAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        CancellationToken cancellationToken)
    {
        MarkProcessedCallCount++;
        if (RejectNextMarkProcessed)
        {
            RejectNextMarkProcessed = false;
            return false;
        }

        return await Inner.MarkProcessedAsync(messageId, claimToken, workerId, cancellationToken);
    }

    public Task<bool> MarkRetryAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        int retryCount,
        DateTime nextAttemptAtUtc,
        string error,
        CancellationToken cancellationToken)
        => Inner.MarkRetryAsync(
            messageId,
            claimToken,
            workerId,
            retryCount,
            nextAttemptAtUtc,
            error,
            cancellationToken);

    public Task<bool> MarkDeadLetterAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        string error,
        CancellationToken cancellationToken)
        => Inner.MarkDeadLetterAsync(messageId, claimToken, workerId, error, cancellationToken);
}
