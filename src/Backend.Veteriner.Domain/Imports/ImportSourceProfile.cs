namespace Backend.Veteriner.Domain.Imports;

/// <summary>
/// Kliniğin geldiği program (TIER-021): içe aktarmada hazır sütun eşlemesini seçer, geçiş desteği talebinde programı belirtir.
/// Eşlemelerin kendisi uygulama katmanındadır (<c>ImportSourceProfiles</c>). Sayısal değerler kalıcıdır.
/// </summary>
public enum ImportSourceProfile
{
    /// <summary>Genel Excel / CSV ya da listede olmayan program; yalnız genel eş anlamlı başlıklar.</summary>
    Generic = 0,
    Kolayvet = 1,
    EVet = 2,
}
