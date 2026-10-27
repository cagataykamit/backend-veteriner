namespace Backend.Veteriner.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    /// <summary>False ise OutboxProcessor hiç çalışmaz (ör. development DB rahatlatma).</summary>
    public bool Enabled { get; set; } = true;

    /// Maksimum tekrar deneme say�s� (sonras�nda dead-letter)
    public int MaxRetryCount { get; set; } = 10;

    /// Exponential backoff i�in taban gecikme (saniye)
    public int BaseDelaySeconds { get; set; } = 5;

    /// Batch boyutu (her d�ng�de ka� mesaj)
    public int BatchSize { get; set; } = 50;

    /// D�ng� periyodu (saniye)
    public int LoopIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Claim (lease) süresi (saniye): mesajı alan instance bu süre içinde sonucu yazmazsa (ör. çökme) mesaj başka
    /// instance tarafından yeniden alınır. Bir partinin gönderim süresinden uzun olmalı.
    /// </summary>
    public int ClaimLeaseSeconds { get; set; } = 300;
}
