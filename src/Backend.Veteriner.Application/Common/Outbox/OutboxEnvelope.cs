namespace Backend.Veteriner.Application.Common.Outbox;

/// Tip adı + JSON payload taşıyan sade zarf
public sealed class OutboxEnvelope
{
    public required string Type { get; init; }  // örn: "Email"
    public required string Payload { get; init; } // JSON string

    public Guid? AppointmentId { get; init; }

    public long? AppointmentSequence { get; init; }
}
