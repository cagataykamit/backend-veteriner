namespace Backend.Veteriner.Application.Projections.Pets;

/// <summary>
/// Backfill sırasında tek bir Command DB <c>Pets</c> satırı için alınacak karar.
/// </summary>
public enum PetReadModelBackfillAction
{
    /// <summary>Query DB'de karşılık gelen <c>PetReadModel</c> yok → yeni satır eklenir.</summary>
    Insert,

    /// <summary>Mevcut satır var ve backfill snapshot'ı en az onun kadar günceldir → satır update edilir.</summary>
    Update,

    /// <summary>
    /// Mevcut satır, backfill snapshot'ından daha güncel bir event ile yazılmıştır
    /// (<c>LastEventOccurredAtUtc</c> daha yeni). Veri korunur; backfill ezmez.
    /// </summary>
    SkipStale
}
