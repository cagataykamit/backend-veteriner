namespace Backend.Veteriner.Application.Common.Outbox;

public interface IOutboxBuffer
{
    Task EnqueueAsync(
        string type,
        string payload,
        CancellationToken ct = default,
        Guid? appointmentId = null,
        long? appointmentSequence = null);

    /// Drain: mevcut batch'i geri döndürür ve buffer'ı temizler.
    IReadOnlyList<OutboxEnvelope> Drain();
}
