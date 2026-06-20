namespace Backend.Veteriner.Application.Projections.Pets;

/// <summary>
/// Command DB outbox'taki pet integration event'lerini (<c>pet.created.v1</c> / <c>pet.updated.v1</c>)
/// Query DB <c>PetReadModels</c> tablosuna uygular.
/// </summary>
public interface IPetProjectionProcessor
{
    /// <summary>
    /// Hazır pet outbox mesajlarını işler; işlenen (processed/duplicate/stale) mesaj sayısını döner.
    /// </summary>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}
