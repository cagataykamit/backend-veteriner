using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.Vaccinations;

public enum VaccinationListSortField
{
    DueAtUtc,
    AppliedAtUtc
}

/// <summary>
/// Aşı listesi sıralama beyaz listesi. <c>sort</c> boşsa varsayılan: birleşik tarih (<c>AppliedAtUtc ?? DueAtUtc</c>) azalan, sonra <c>Id</c> azalan.
/// Seçilen tarih alanı boş olan kayıtlar her iki yönde de sona gelir. <c>nextDueAtUtc</c> mevcut istemciler için <c>dueAtUtc</c> eş adıdır.
/// </summary>
public static class VaccinationListSort
{
    public static readonly ListSortCatalog<VaccinationListSortField> Catalog = new(
        ("dueAtUtc", VaccinationListSortField.DueAtUtc),
        ("appliedAtUtc", VaccinationListSortField.AppliedAtUtc),
        ("nextDueAtUtc", VaccinationListSortField.DueAtUtc));
}
