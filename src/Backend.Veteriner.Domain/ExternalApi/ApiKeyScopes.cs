namespace Backend.Veteriner.Domain.ExternalApi;

/// <summary>
/// Dış API anahtar kapsamları (TIER-009; docs/EXTERNAL_API_CONTRACT.md). İlk sürümde yalnız okuma; her uç tek kapsam ister ve anahtar
/// yalnız seçilen kapsamları taşır. Yazma kapsamı yoktur.
/// </summary>
public static class ApiKeyScopes
{
    public const string ClientsRead = "clients:read";
    public const string PetsRead = "pets:read";
    public const string AppointmentsRead = "appointments:read";
    public const string VaccinationsRead = "vaccinations:read";

    public static IReadOnlyList<string> All { get; } = [ClientsRead, PetsRead, AppointmentsRead, VaccinationsRead];

    /// <summary>Saklanan biçim: tekrarsız, sıralı, boşlukla ayrılmış (OAuth <c>scope</c> gibi).</summary>
    public const char Separator = ' ';
}
