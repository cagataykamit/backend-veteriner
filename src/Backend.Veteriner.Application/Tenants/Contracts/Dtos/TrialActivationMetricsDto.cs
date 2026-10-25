namespace Backend.Veteriner.Application.Tenants.Contracts.Dtos;

/// <summary>
/// Deneme aktivasyon özeti (TRIAL-008; <c>docs/TRIAL_ACTIVATION_METRICS_API_CONTRACT.md</c> §3). Oranlar 0–1 arası;
/// payda sıfırsa <c>null</c>. Kohort = dönemde deneme başlatan kiracılar; tüm oranların paydası <see cref="TrialStarts"/>.
/// </summary>
public sealed record TrialActivationMetricsDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int TrialStarts,
    TrialMetricRatioDto ExampleClinic,
    TrialActivationSummaryDto Activation,
    TrialMetricRatioDto Conversion,
    IReadOnlyList<TrialFirstActionBreakdownDto> FirstActionBreakdown,
    IReadOnlyList<TrialOnboardingComparisonDto> ByOnboarding);

public sealed record TrialMetricRatioDto(int Count, decimal? Rate);

public sealed record TrialActivationSummaryDto(
    int Count,
    decimal? Rate,
    int Within10MinutesCount,
    decimal? Within10MinutesRate,
    decimal? MedianMinutesToFirstAction);

/// <param name="Kind"><c>ActivationSignalKind</c> adı.</param>
public sealed record TrialFirstActionBreakdownDto(string Kind, int Count);

/// <param name="Onboarding"><c>ExampleClinic</c> | <c>EmptyClinic</c>.</param>
public sealed record TrialOnboardingComparisonDto(
    string Onboarding,
    int TrialStarts,
    decimal? ActivationRate,
    decimal? Within10MinutesRate,
    decimal? ConversionRate);
