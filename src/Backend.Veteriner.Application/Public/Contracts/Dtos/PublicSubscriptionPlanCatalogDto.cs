namespace Backend.Veteriner.Application.Public.Contracts.Dtos;

/// <summary>
/// Tanıtım sitesi paketler sayfası (WEB-007): plan kataloğu ve liste fiyatları. Tutarlar kuruş, KDV hariç
/// (<see cref="VatIncluded"/>); yıllık tutar <see cref="YearlyBilledMonths"/> aylık ücrettir.
/// </summary>
public sealed record PublicSubscriptionPlanCatalogDto(
    string Currency,
    bool VatIncluded,
    int YearlyBilledMonths,
    IReadOnlyList<PublicSubscriptionPlanDto> Plans);

/// <summary>Fiyat yapılandırılmamışsa <see cref="MonthlyPriceMinor"/> ve <see cref="YearlyPriceMinor"/> <c>null</c> döner.</summary>
public sealed record PublicSubscriptionPlanDto(
    string PlanCode,
    string Name,
    int MaxUsers,
    long? MonthlyPriceMinor,
    long? YearlyPriceMinor);
