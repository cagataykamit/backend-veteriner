namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Panel genelinde kiracı yazılabilirlik / salt okunur özeti.
/// Abonelik fiyatı, checkout, bekleyen plan değişikliği veya ödeme detayı içermez.
/// <see cref="PlanFeatures"/> paket özelliklerinin durumu (TIER-001); pasif kiracıda boş.
/// </summary>
public sealed record TenantAccessStateDto(
    Guid TenantId,
    bool IsReadOnly,
    string? ReasonCode,
    string? Message)
{
    public string? PlanCode { get; init; }
    public IReadOnlyList<TenantPlanFeatureDto> PlanFeatures { get; init; } = [];
}

/// <summary>Kilitli özellikte panelin "üst pakete geç" ekranı <see cref="RequiredPlanCode"/> paketini gösterir.</summary>
public sealed record TenantPlanFeatureDto(string Feature, bool IsEnabled, string RequiredPlanCode);
