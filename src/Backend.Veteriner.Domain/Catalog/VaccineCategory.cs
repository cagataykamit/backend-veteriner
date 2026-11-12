namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Aşı tanımının klinik sınıfı. Aşılar için WSAVA 2024 sınıflaması (çekirdek / çekirdek dışı / önerilmeyen);
/// antiparaziter uygulamalar aşı değildir, karnede aşıyla birlikte izlendiği için aynı katalogda ayrı sınıftadır (ESCCAP).
/// <see cref="VaccineDefinition.IsCore"/> ile karıştırılmaz: o alan sistem satırı kilidini taşır.
/// </summary>
public enum VaccineCategory
{
    Core = 1,
    NonCore = 2,
    NotRecommended = 3,
    ParasiteControl = 4,
}
