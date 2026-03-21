namespace Backend.Veteriner.Application.Common.Outbox;

/// Tip ad� + JSON payload ta��yan sade zarf
public sealed class OutboxEnvelope
{
    public required string Type { get; init; }  // �rn: "Email"
    public required string Payload { get; init; } // JSON string
}
