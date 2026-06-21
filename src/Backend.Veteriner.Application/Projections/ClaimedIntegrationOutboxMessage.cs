namespace Backend.Veteriner.Application.Projections;

/// <summary>
/// Atomik claim sonrası işlenecek integration outbox satırı (client/pet projection).
/// </summary>
public sealed record ClaimedIntegrationOutboxMessage(
    Guid Id,
    string Type,
    string Payload,
    DateTime CreatedAtUtc,
    int RetryCount,
    Guid ClaimToken,
    string ClaimedBy,
    DateTime ClaimedAtUtc,
    DateTime LeaseExpiresAtUtc);
