using System.Text.Json;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Outbox;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Mailing;

public sealed class TransactionalEmailSender : IEmailSender
{
    private readonly IOutboxBuffer _buffer;
    private readonly ILogger<TransactionalEmailSender> _logger;

    public TransactionalEmailSender(IOutboxBuffer buffer, ILogger<TransactionalEmailSender> logger)
    {
        _buffer = buffer;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default, bool isHtml = false)
    {
        var payload = new EmailOutboxPayload
        {
            To = to,
            Subject = subject,
            Body = body,
            IsHtml = isHtml
        };

        var json = JsonSerializer.Serialize(payload);
  
        await _buffer.EnqueueAsync(OutboxMessageTypes.Email, json, ct);
        _logger.LogInformation("TransactionalEmailSender: Enqueued to buffer (len={Len})", json.Length);
    }
}
