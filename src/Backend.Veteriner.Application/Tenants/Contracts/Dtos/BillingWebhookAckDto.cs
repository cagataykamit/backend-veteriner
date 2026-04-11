namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

public sealed record BillingWebhookAckDto(
    bool Duplicate,
    bool Processed,
    string? ProviderEventId);
