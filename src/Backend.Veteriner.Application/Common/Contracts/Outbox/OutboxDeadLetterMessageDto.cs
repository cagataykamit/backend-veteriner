namespace Backend.Veteriner.Application.Common.Contracts.Outbox;

/// <summary><c>GET /api/v1/outbox/dead</c> satırı (alan sırası yanıt sözleşmesidir).</summary>
public sealed record OutboxDeadLetterMessageDto(
    Guid Id,
    string Type,
    int RetryCount,
    DateTime CreatedAtUtc,
    DateTime? DeadLetterAtUtc,
    string? LastError);
