namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Payment list read-model backfill (CQRS-14F) sırasında tek bir Command DB <c>Payments</c> satırı için
/// alınacak karar.
/// </summary>
public enum PaymentReadModelBackfillAction
{
    /// <summary>Query DB'de karşılık gelen <c>PaymentReadModel</c> yok → yeni satır eklenir.</summary>
    Insert,

    /// <summary>Mevcut satır var ve backfill snapshot'ı en az onun kadar günceldir → satır update edilir.</summary>
    Update,

    /// <summary>
    /// Mevcut satır, backfill snapshot'ından daha güncel bir projection event'i ile yazılmıştır
    /// (<c>LastEventOccurredAtUtc</c> daha yeni). Veri korunur; backfill ezmez.
    /// </summary>
    SkipStale
}
