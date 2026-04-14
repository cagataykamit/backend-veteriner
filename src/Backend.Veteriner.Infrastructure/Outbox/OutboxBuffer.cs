// src/Backend.Veteriner.Infrastructure/Outbox/OutboxBuffer.cs
using System.Collections.Concurrent;
using Backend.Veteriner.Application.Common.Outbox;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Outbox;

/// Scoped: Her HTTP iste�i i�in 1 instance
public sealed class OutboxBuffer : IOutboxBuffer
{
    private readonly List<OutboxEnvelope> _items = new();
    private readonly ILogger<OutboxBuffer> _logger;

    public OutboxBuffer(ILogger<OutboxBuffer> logger) => _logger = logger;

    public Task EnqueueAsync(string type, string payload, CancellationToken ct = default)
    {
        // basit guard
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("type required", nameof(type));
        if (payload is null) throw new ArgumentNullException(nameof(payload));

        _items.Add(new OutboxEnvelope { Type = type, Payload = payload });
        _logger.LogDebug("OutboxBuffer: Enqueued Type={Type}, Len={Len}, Count={Count}",
            type, payload.Length, _items.Count);

        return Task.CompletedTask;
    }

    public IReadOnlyList<OutboxEnvelope> Drain()
    {
        if (_items.Count == 0)
        {
            _logger.LogDebug("OutboxBuffer: Drain called, nothing to drain");
            return Array.Empty<OutboxEnvelope>();
        }

        var batch = _items.ToArray();
        _items.Clear();

        _logger.LogInformation("OutboxBuffer: Drained {Count} item(s)", batch.Length);
        return batch;
    }
}
