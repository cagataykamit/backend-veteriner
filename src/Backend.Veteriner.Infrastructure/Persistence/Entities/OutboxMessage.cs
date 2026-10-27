namespace Backend.Veteriner.Infrastructure.Persistence.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Mesaj tipi � �rn: "email", "sms", "audit"
    /// </summary>
    public string Type { get; set; } = default!;

    /// <summary>
    /// JSON payload (serialized DTO)
    /// </summary>
    public string Payload { get; set; } = default!;

    /// <summary>
    /// Mesaj�n olu�turuldu�u UTC zaman
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = TimeProvider.System.GetUtcNow().UtcDateTime;

    /// <summary>
    /// ��lendi�i zaman (e�er i�lendi)
    /// </summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>
    /// Son hata (null => ba�ar�l�)
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Toplam tekrar say�s� (retry counter)
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// Bir sonraki deneme zaman� (UTC)
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>
    /// �ok fazla hata ald�ysa dead-letter olarak i�aretlenir
    /// </summary>
    public DateTime? DeadLetterAtUtc { get; set; }

    /// <summary>
    /// Detayl� hata ��kt�s� (stack trace veya exception.ToString)
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Korelasyon kimli�i (istek zincirini izlemek i�in)
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Orijinal iste�in izleme kimli�i (OpenTelemetry ActivityTraceId)
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>Appointment integration event'leri için sıralama anahtarı (nullable — diğer outbox tipleri).</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>Appointment mutation sequence; projection ordering için.</summary>
    public long? AppointmentSequence { get; set; }

    /// <summary>Claim eden worker kimliği (process-scoped; max 128 karakter).</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>Atomik claim fence token; completion/retry/dead-letter güncellemelerinde doğrulanır.</summary>
    public Guid? ClaimToken { get; set; }

    /// <summary>Claim anı (UTC).</summary>
    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>Lease bitiş zamanı (UTC); süresi dolunca başka worker reclaim edebilir.</summary>
    public DateTime? LeaseExpiresAtUtc { get; set; }
}
