namespace Backend.Veteriner.Application.Projections.Clients;

/// <summary>
/// Backfill sırasında tek bir Command DB <c>Clients</c> satırı için alınacak karar.
/// </summary>
public enum ClientReadModelBackfillAction
{
    /// <summary>Query DB'de karşılık gelen <c>ClientReadModel</c> yok → yeni satır eklenir.</summary>
    Insert,

    /// <summary>Mevcut satır var ve backfill snapshot'ı en az onun kadar günceldir → satır update edilir.</summary>
    Update,

    /// <summary>
    /// Mevcut satır, backfill snapshot'ından daha güncel bir event ile yazılmıştır
    /// (<c>LastEventOccurredAtUtc</c> daha yeni). Veri korunur; backfill ezmez.
    /// </summary>
    SkipStale
}
