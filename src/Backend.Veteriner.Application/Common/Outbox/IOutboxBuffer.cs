// src/Backend.Veteriner.Application/Common/Outbox/IOutboxBuffer.cs
namespace Backend.Veteriner.Application.Common.Outbox;

public interface IOutboxBuffer
{
    Task EnqueueAsync(string type, string payload, CancellationToken ct = default);
    /// Drain: mevcut batch�i geri d�nd�r�r ve buffer�� temizler.
    IReadOnlyList<OutboxEnvelope> Drain();
}
