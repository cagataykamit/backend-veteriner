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

/// <summary>
/// Fiyat yapılandırılmamışsa <see cref="MonthlyPriceMinor"/> ve <see cref="YearlyPriceMinor"/> <c>null</c> döner.
/// <see cref="MonthlyAiCredits"/>: paketin klinik başına aylık yapay zekâ kredisi hediyesi (ADR-044 madde 5; <c>Ai:PlanMonthlyCredits</c>).
/// </summary>
public sealed record PublicSubscriptionPlanDto(
    string PlanCode,
    string Name,
    int MaxUsers,
    long? MonthlyPriceMinor,
    long? YearlyPriceMinor,
    int MonthlyAiCredits);
