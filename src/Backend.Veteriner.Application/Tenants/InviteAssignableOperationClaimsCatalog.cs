using System.Linq;

namespace Backend.Veteriner.Application.Tenants;

/// <summary>
/// Davet akışında atanabilecek operation claim adları (whitelist).
/// Teknik/internal roller burada listelenmez; sadece bu isimler GET assignable + POST invite ile kabul edilir.
/// </summary>
public static class InviteAssignableOperationClaimsCatalog
{
    /// <summary>Sıralı liste (UI sırası).</summary>
    public static IReadOnlyList<string> NamesInDisplayOrder { get; } =
    [
        "Admin",
        "ClinicAdmin",
        "Veteriner",
        "Sekreter",
    ];

    public static bool IsAssignableName(string? operationClaimName)
    {
        if (string.IsNullOrWhiteSpace(operationClaimName))
            return false;
        return NamesInDisplayOrder.Any(n =>
            string.Equals(n, operationClaimName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
