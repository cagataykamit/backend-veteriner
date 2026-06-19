using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Projections.Appointments;

internal sealed class TestAppointmentOutboxClaimRepository : IAppointmentOutboxClaimRepository
{
    private readonly IServiceProvider _serviceProvider;
    private IAppointmentOutboxClaimRepository? _inner;

    public TestAppointmentOutboxClaimRepository(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public bool RejectNextMarkProcessed { get; set; }

    public bool RejectNextMarkRetry { get; set; }

    public bool RejectNextMarkDeadLetter { get; set; }

    public int MarkProcessedCallCount { get; private set; }

    public int MarkRetryCallCount { get; private set; }

    public int MarkDeadLetterCallCount { get; private set; }

    private IAppointmentOutboxClaimRepository Inner
        => _inner ??= ActivatorUtilities.CreateInstance<SqlAppointmentOutboxClaimRepository>(_serviceProvider);

    public Task<IReadOnlyList<ClaimedAppointmentOutboxMessage>> ClaimNextBatchAsync(
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

    public async Task<bool> MarkRetryAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        int retryCount,
        DateTime nextAttemptAtUtc,
        string error,
        CancellationToken cancellationToken)
    {
        MarkRetryCallCount++;
        if (RejectNextMarkRetry)
        {
            RejectNextMarkRetry = false;
            return false;
        }

        return await Inner.MarkRetryAsync(
            messageId,
            claimToken,
            workerId,
            retryCount,
            nextAttemptAtUtc,
            error,
            cancellationToken);
    }

    public async Task<bool> MarkDeadLetterAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        string error,
        CancellationToken cancellationToken)
    {
        MarkDeadLetterCallCount++;
        if (RejectNextMarkDeadLetter)
        {
            RejectNextMarkDeadLetter = false;
            return false;
        }

        return await Inner.MarkDeadLetterAsync(messageId, claimToken, workerId, error, cancellationToken);
    }
}
