using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.Hospitalizations;

public enum HospitalizationListSortField
{
    AdmittedAtUtc
}

/// <summary>Yatış listesi sıralama beyaz listesi. <c>sort</c> boşsa varsayılan: <c>AdmittedAtUtc</c> azalan, sonra <c>Id</c> azalan.</summary>
public static class HospitalizationListSort
{
    public static readonly ListSortCatalog<HospitalizationListSortField> Catalog = new(
        ("admittedAtUtc", HospitalizationListSortField.AdmittedAtUtc));
}
