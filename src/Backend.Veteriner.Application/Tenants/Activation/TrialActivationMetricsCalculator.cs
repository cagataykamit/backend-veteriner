using Backend.Veteriner.Application.Tenants.Contracts.Dtos;

namespace Backend.Veteriner.Application.Tenants.Activation;

/// <summary>
/// Kohort satırlarından deneme aktivasyon özetini hesaplar (TRIAL-008, sözleşme §3). Saf fonksiyon; veritabanı yok.
/// Kurallar: oran = sayı / deneme sayısı (payda 0 → null); "10 dk içinde" eşitlik dahil; medyan çift elemanda ortadaki
/// ikisinin ortalaması, 1 ondalık; dağılım yalnız sayısı > 0 olan türler, azalan.
/// </summary>
public static class TrialActivationMetricsCalculator
{
    public const string OnboardingExampleClinic = "ExampleClinic";
    public const string OnboardingEmptyClinic = "EmptyClinic";

    public static readonly TimeSpan QuickActivationWindow = TimeSpan.FromMinutes(10);

    public static TrialActivationMetricsDto Calculate(DateTime fromUtc, DateTime toUtc, IReadOnlyList<TrialCohortRow> cohort)
    {
        var trialStarts = cohort.Count;
        var activated = cohort.Where(IsActivated).ToList();
        var quick = activated.Count(IsQuickActivation);
        var exampleCount = cohort.Count(r => r.ExampleClinicLoadedAtUtc.HasValue);
        var converted = cohort.Count(IsConverted);

        var breakdown = activated
            .GroupBy(r => r.FirstActionKind!.Value)
            .Select(g => new TrialFirstActionBreakdownDto(g.Key.ToString(), g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ToList();

        return new TrialActivationMetricsDto(
            fromUtc,
            toUtc,
            trialStarts,
            new TrialMetricRatioDto(exampleCount, Rate(exampleCount, trialStarts)),
            new TrialActivationSummaryDto(
                activated.Count,
                Rate(activated.Count, trialStarts),
                quick,
                Rate(quick, trialStarts),
                MedianMinutes(activated)),
            new TrialMetricRatioDto(converted, Rate(converted, trialStarts)),
            breakdown,
            [
                Compare(OnboardingExampleClinic, cohort.Where(r => r.ExampleClinicLoadedAtUtc.HasValue).ToList()),
                Compare(OnboardingEmptyClinic, cohort.Where(r => !r.ExampleClinicLoadedAtUtc.HasValue).ToList()),
            ]);
    }

    private static TrialOnboardingComparisonDto Compare(string onboarding, List<TrialCohortRow> rows)
    {
        var total = rows.Count;
        var activated = rows.Where(IsActivated).ToList();
        return new TrialOnboardingComparisonDto(
            onboarding,
            total,
            Rate(activated.Count, total),
            Rate(activated.Count(IsQuickActivation), total),
            Rate(rows.Count(IsConverted), total));
    }

    private static bool IsActivated(TrialCohortRow r) => r.FirstActionAtUtc.HasValue && r.FirstActionKind.HasValue;

    private static bool IsConverted(TrialCohortRow r) => r.PaidActivatedAtUtc.HasValue;

    private static bool IsQuickActivation(TrialCohortRow r)
        => r.FirstActionAtUtc!.Value - r.TrialStartsAtUtc <= QuickActivationWindow;

    private static decimal? Rate(int count, int total)
        => total == 0 ? null : Math.Round((decimal)count / total, 4, MidpointRounding.AwayFromZero);

    private static decimal? MedianMinutes(List<TrialCohortRow> activated)
    {
        if (activated.Count == 0)
            return null;

        var minutes = activated
            .Select(r => (decimal)(r.FirstActionAtUtc!.Value - r.TrialStartsAtUtc).TotalMinutes)
            .OrderBy(m => m)
            .ToList();

        var mid = minutes.Count / 2;
        var median = minutes.Count % 2 == 1 ? minutes[mid] : (minutes[mid - 1] + minutes[mid]) / 2m;
        return Math.Round(median, 1, MidpointRounding.AwayFromZero);
    }
}
