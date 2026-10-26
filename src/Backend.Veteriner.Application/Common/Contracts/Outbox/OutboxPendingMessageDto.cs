namespace Backend.Veteriner.Application.Common.Contracts.Outbox;

/// <summary><c>GET /api/v1/outbox/pending</c> satırı (alan sırası yanıt sözleşmesidir).</summary>
public sealed record OutboxPendingMessageDto(
    Guid Id,
    string Type,
    int RetryCount,
    DateTime CreatedAtUtc,
    DateTime? NextAttemptAtUtc,
    string? LastError);
