namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Panel genelinde kiracı yazılabilirlik / salt okunur özeti.
/// Abonelik fiyatı, checkout, bekleyen plan değişikliği veya ödeme detayı içermez.
/// </summary>
public sealed record TenantAccessStateDto(
    Guid TenantId,
    bool IsReadOnly,
    string? ReasonCode,
    string? Message);
