using Backend.Veteriner.Application.Common.Outbox;

namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IOutbox
{
    Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default);
    Task EnqueueEmailAsync(EmailOutboxPayload email, CancellationToken ct = default);
}
