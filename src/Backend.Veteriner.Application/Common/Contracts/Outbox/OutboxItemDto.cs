namespace Backend.Veteriner.Application.Common.Contracts.Outbox;

public sealed class OutboxItemDto
{
    public Guid Id { get; init; }
    public string Type { get; init; } = default!;
    public DateTime CreatedAtUtc { get; init; }
    public int RetryCount { get; init; }
    public DateTime? NextAttemptAtUtc { get; init; }
    public string? LastError { get; init; }
    public DateTime? DeadLetterAtUtc { get; init; }
}
