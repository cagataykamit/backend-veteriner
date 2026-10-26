namespace Backend.Veteriner.Application.Pets.IntegrationEvents;

/// <summary>
/// Pet kalıcı olarak silindiğinde üretilir (içe aktarma geri alması, IMPORT_API_CONTRACT.md §9; örnek klinik temizliği, EXAMPLE_CLINIC_API_CONTRACT.md §10).
/// Read-model satırını silmek için kimlik yeterlidir; kişisel veri taşımaz.
/// </summary>
public sealed record PetDeletedIntegrationEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid PetId,
    Guid TenantId);
