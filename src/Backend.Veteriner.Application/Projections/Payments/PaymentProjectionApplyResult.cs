namespace Backend.Veteriner.Application.Projections.Payments;

/// <summary>
/// Tek bir payment integration event'inin Query DB'ye uygulanma sonucu.
/// </summary>
public sealed class PaymentProjectionApplyResult
{
    private PaymentProjectionApplyResult(bool isDuplicate, bool isStale)
    {
        IsDuplicate = isDuplicate;
        IsStale = isStale;
    }

    /// <summary>Aynı (EventId, ConsumerName) daha önce işlenmiş; read-model'e dokunulmadı.</summary>
    public bool IsDuplicate { get; }

    /// <summary>
    /// Event idempotency'den geçti ama taşıdığı <c>OccurredAtUtc</c> mevcut contribution'dan eski olduğu için
    /// (out-of-order) contribution ve daily aggregate korundu; yalnızca dedup satırı yazıldı.
    /// </summary>
    public bool IsStale { get; }

    public static PaymentProjectionApplyResult DuplicateSkipped() => new(isDuplicate: true, isStale: false);

    public static PaymentProjectionApplyResult StaleSkipped() => new(isDuplicate: false, isStale: true);

    public static PaymentProjectionApplyResult Applied() => new(isDuplicate: false, isStale: false);
}
