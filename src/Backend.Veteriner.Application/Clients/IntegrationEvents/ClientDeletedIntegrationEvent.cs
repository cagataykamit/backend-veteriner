namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// Client kalıcı olarak silindiğinde üretilir (yalnız içe aktarma geri alması; IMPORT_API_CONTRACT.md §9).
/// Read-model satırını silmek için kimlik yeterlidir; kişisel veri taşımaz.
/// </summary>
public sealed record ClientDeletedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ClientId,
    Guid TenantId);
