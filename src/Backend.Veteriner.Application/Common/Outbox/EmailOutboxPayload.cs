namespace Backend.Veteriner.Application.Common.Outbox;

public sealed class EmailOutboxPayload
{
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public bool IsHtml { get; init; } = false;
}
