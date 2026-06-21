namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Command DB outbox'taki payment integration event'lerini (<c>payment.created.v1</c> / <c>payment.updated.v1</c>)
/// Query DB finance read-model'lerine uygular.
/// </summary>
public interface IPaymentProjectionProcessor
{
    /// <summary>
    /// Hazır payment outbox mesajlarını işler; işlenen (processed/duplicate/stale) mesaj sayısını döner.
    /// </summary>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}
