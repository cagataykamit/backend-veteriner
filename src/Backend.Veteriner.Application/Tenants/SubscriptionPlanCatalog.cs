using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Statik plan kataloğu; ileride DB/ödeme ile genişletilebilir. Paket → özellik kuralının tek yeri (TIER-001,
/// docs/PLAN_FEATURES_API_CONTRACT.md); kiracı için karar <see cref="TenantPlanFeatureEvaluator"/>.
/// </summary>
public static class SubscriptionPlanCatalog
{
    public sealed record PlanEntry(
        SubscriptionPlanCode Code,
        string Name,
        string? Description,
        int MaxUsers,
        IReadOnlySet<PlanFeature> Features);

    public static IReadOnlyList<PlanEntry> All { get; } =
    [
        new(SubscriptionPlanCode.Basic, "Basic", "Temel özellikler.", 3, new HashSet<PlanFeature>()),
        new(SubscriptionPlanCode.Pro, "Pro", "Gelişmiş özellikler.", 5,
            new HashSet<PlanFeature> { PlanFeature.OwnerPortal, PlanFeature.PortalAppointmentRequests }),
        new(SubscriptionPlanCode.Premium, "Premium", "Kurumsal özellikler.", 15,
            new HashSet<PlanFeature> { PlanFeature.OwnerPortal, PlanFeature.PortalAppointmentRequests, PlanFeature.MultiClinic }),
    ];

    public static string GetName(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.Name ?? code.ToString();

    public static string? GetDescription(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.Description;

    public static string ToApiCode(SubscriptionPlanCode code) => code.ToString();

    public static int GetMaxUsers(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.MaxUsers
           ?? throw new InvalidOperationException($"Plan için maxUsers tanımlı değil: {code}.");

    /// <summary>Özellik bu pakette var mı; katalogda olmayan paket kilitli sayılır.</summary>
    public static bool Allows(SubscriptionPlanCode code, PlanFeature feature)
        => All.FirstOrDefault(x => x.Code == code)?.Features.Contains(feature) == true;

    /// <summary>Özelliği içeren en küçük paket (katalog sırası küçükten büyüğe).</summary>
    public static SubscriptionPlanCode MinimumPlanFor(PlanFeature feature)
        => All.First(x => x.Features.Contains(feature)).Code;

    public static bool TryParseApiCode(string? value, out SubscriptionPlanCode code)
    {
        code = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!Enum.TryParse<SubscriptionPlanCode>(value.Trim(), ignoreCase: true, out var parsed))
            return false;

        if (!All.Any(x => x.Code == parsed))
            return false;

        code = parsed;
        return true;
    }
}
