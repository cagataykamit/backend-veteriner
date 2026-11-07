using Backend.Veteriner.Domain.ExternalApi;

namespace Backend.Veteriner.Application.ExternalApi;

/// <summary>
/// Dış API kimlik şeması, başlığı, politika ve claim adları (TIER-009). Personel JWT'si ve portal şemasından ayrıdır: dış uçlar yalnız
/// bu şemayı, panel uçları yalnız personel JWT'sini doğrular.
/// </summary>
public static class ExternalApiAuth
{
    public const string Scheme = "ExternalApiKey";
    public const string HeaderName = "X-Api-Key";

    /// <summary>Tüm dış uçların politikası: <see cref="Scheme"/> + <see cref="PrincipalTypeClaim"/> = <see cref="ApiKeyPrincipalType"/>.</summary>
    public const string Policy = "ExternalApiKey";

    public const string PrincipalTypeClaim = "principal_type";
    public const string ApiKeyPrincipalType = "api_key";
    public const string KeyIdClaim = "api_key_id";
    public const string ScopeClaim = "scope";

    /// <summary>Kapsam başına politika adı; uç bu politikayı ister, anahtarda kapsam yoksa 403 <see cref="ExternalApiErrors.ScopeMissingCode"/>.</summary>
    public static string ScopePolicy(string scope) => $"{Policy}:{scope}";

    public static class Policies
    {
        public const string ClientsRead = Policy + ":" + ApiKeyScopes.ClientsRead;
        public const string PetsRead = Policy + ":" + ApiKeyScopes.PetsRead;
        public const string AppointmentsRead = Policy + ":" + ApiKeyScopes.AppointmentsRead;
        public const string VaccinationsRead = Policy + ":" + ApiKeyScopes.VaccinationsRead;
    }
}
