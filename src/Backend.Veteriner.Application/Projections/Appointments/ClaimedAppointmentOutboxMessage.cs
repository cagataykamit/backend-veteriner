namespace Backend.Veteriner.Application.Projections.Appointments;

/// <summary>
/// Atomik claim sonrası işlenecek appointment outbox satırı.
/// </summary>
public sealed record ClaimedAppointmentOutboxMessage(
    Guid Id,
    string Type,
    string Payload,
    DateTime CreatedAtUtc,
    Guid AppointmentId,
    long AppointmentSequence,
    int RetryCount,
    Guid ClaimToken,
    string ClaimedBy,
    DateTime ClaimedAtUtc,
    DateTime LeaseExpiresAtUtc);
