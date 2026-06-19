namespace Backend.Veteriner.Application.Projections.Appointments;

public interface IAppointmentOutboxClaimRepository
{
    Task<IReadOnlyList<ClaimedAppointmentOutboxMessage>> ClaimNextBatchAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> MarkProcessedAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        CancellationToken cancellationToken);

    Task<bool> MarkRetryAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        int retryCount,
        DateTime nextAttemptAtUtc,
        string error,
        CancellationToken cancellationToken);

    Task<bool> MarkDeadLetterAsync(
        Guid messageId,
        Guid claimToken,
        string workerId,
        string error,
        CancellationToken cancellationToken);
}
