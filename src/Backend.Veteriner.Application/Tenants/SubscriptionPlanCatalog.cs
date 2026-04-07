using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>Statik plan kataloğu; ileride DB/ödeme ile genişletilebilir.</summary>
public static class SubscriptionPlanCatalog
{
    public sealed record PlanEntry(SubscriptionPlanCode Code, string Name, string? Description, int MaxUsers);

    public static IReadOnlyList<PlanEntry> All { get; } =
    [
        new(SubscriptionPlanCode.Basic, "Basic", "Temel özellikler.", 3),
        new(SubscriptionPlanCode.Pro, "Pro", "Gelişmiş özellikler.", 10),
        new(SubscriptionPlanCode.Premium, "Premium", "Kurumsal özellikler.", 50),
    ];

    public static string GetName(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.Name ?? code.ToString();

    public static string? GetDescription(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.Description;

    public static string ToApiCode(SubscriptionPlanCode code) => code.ToString();

    public static int GetMaxUsers(SubscriptionPlanCode code)
        => All.FirstOrDefault(x => x.Code == code)?.MaxUsers
           ?? throw new InvalidOperationException($"Plan için maxUsers tanımlı değil: {code}.");

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
