namespace Backend.Veteriner.Application.Projections.Clients;

/// <summary>
/// Tek bir client integration event'inin Query DB'ye uygulanma sonucu.
/// </summary>
public sealed class ClientProjectionApplyResult
{
    private ClientProjectionApplyResult(bool isDuplicate, bool isStale)
    {
        IsDuplicate = isDuplicate;
        IsStale = isStale;
    }

    /// <summary>Aynı (EventId, ConsumerName) daha önce işlenmiş; read-model'e dokunulmadı.</summary>
    public bool IsDuplicate { get; }

    /// <summary>
    /// Event idempotency'den geçti ama taşıdığı <c>OccurredAtUtc</c> mevcut satırınkinden eski olduğu için
    /// (out-of-order) read-model verisi korundu; yalnızca dedup satırı yazıldı.
    /// </summary>
    public bool IsStale { get; }

    public static ClientProjectionApplyResult DuplicateSkipped() => new(isDuplicate: true, isStale: false);

    public static ClientProjectionApplyResult StaleSkipped() => new(isDuplicate: false, isStale: true);

    public static ClientProjectionApplyResult Applied() => new(isDuplicate: false, isStale: false);
}
