using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>Statik plan kataloğu; ileride DB/ödeme ile genişletilebilir.</summary>
public static class SubscriptionPlanCatalog
{
    public sealed record PlanEntry(SubscriptionPlanCode Code, string Name, string? Description);

    public static IReadOnlyList<PlanEntry> All { get; } =
    [
        new(SubscriptionPlanCode.Basic, "Basic", "Temel özellikler."),
        new(SubscriptionPlanCode.Pro, "Pro", "Gelişmiş özellikler."),
        new(SubscriptionPlanCode.Premium, "Premium", "Kurumsal özellikler."),
    ];

    public static string GetName(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.Name ?? code.ToString();

    public static string? GetDescription(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.Description;

    public static string ToApiCode(SubscriptionPlanCode code) => code.ToString();
}
