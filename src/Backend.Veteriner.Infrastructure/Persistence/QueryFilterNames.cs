namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// EF adlı sorgu filtreleri (ADR-040). <see cref="Tenant"/> kiracı filtresidir ve yalnız <c>ITenantScope.AsSystem</c>
/// ile kalkar; <c>IgnoreQueryFilters</c> yalnız <see cref="Ignorable"/> listesindekileri kaldırır (parametresiz çağrı
/// yasaklıdır, BannedSymbols).
/// </summary>
public static class QueryFilterNames
{
    public const string Tenant = "Tenant";

    /// <summary>İptal edilen ücret/ödeme ve geri alınan tahsis tüm okuma yüzeylerinden düşer.</summary>
    public const string Voided = "Voided";

    /// <summary><c>IgnoreQueryFilters</c> ile kaldırılabilen filtreler; kiracı filtresi bu listede yoktur.</summary>
    public static readonly IReadOnlyCollection<string> Ignorable = [Voided];
}
