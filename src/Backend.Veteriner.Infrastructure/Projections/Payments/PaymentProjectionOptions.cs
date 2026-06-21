namespace Backend.Veteriner.Infrastructure.Projections.Payments;

public sealed class PaymentProjectionOptions
{
    public const string SectionName = "PaymentProjection";

    /// <summary>False ise hosted service hiç poll etmez (processor manuel çağrılabilir kalır).</summary>
    public bool Enabled { get; set; } = false;

    public int BatchSize { get; set; } = 50;

    public int LoopIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// <c>ProcessedProjectionEvents</c> idempotency consumer adı. Diğer projection consumer'larından
    /// ayrıdır; PK (EventId, ConsumerName) olduğundan bağımsız dedup sağlar.
    /// </summary>
    public string ConsumerName { get; set; } = "payment-finance-v1";

    /// <summary>
    /// Atomik outbox claim/lease kullanımı. Varsayılan kapalı — mevcut FIFO processor davranışı korunur.
    /// </summary>
    public bool ClaimingEnabled { get; set; }

    /// <summary>Claim sonrası lease süresi (saniye).</summary>
    public int LeaseDurationSeconds { get; set; } = 60;

    /// <summary>Her claim döngüsünde alınacak maksimum outbox satırı.</summary>
    public int ClaimBatchSize { get; set; } = 1;

    public const int MaxClaimBatchSize = 50;
}
