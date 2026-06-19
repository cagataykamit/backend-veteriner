namespace Backend.Veteriner.Application.Projections.Clients;

/// <summary>
/// Command DB outbox'taki client integration event'lerini (<c>client.created.v1</c> / <c>client.updated.v1</c>)
/// Query DB <c>ClientReadModels</c> tablosuna uygular.
/// </summary>
public interface IClientProjectionProcessor
{
    /// <summary>
    /// Hazır client outbox mesajlarını işler; işlenen (processed/duplicate/stale) mesaj sayısını döner.
    /// </summary>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}
