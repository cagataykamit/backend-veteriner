namespace Backend.Veteriner.Application.Clients.IntegrationEvents;

/// <summary>
/// Client kalıcı olarak silindiğinde üretilir (içe aktarma geri alması, IMPORT_API_CONTRACT.md §9; örnek klinik temizliği, EXAMPLE_CLINIC_API_CONTRACT.md §10).
/// Read-model satırını silmek için kimlik yeterlidir; kişisel veri taşımaz.
/// </summary>
public sealed record ClientDeletedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid ClientId,
    Guid TenantId);
