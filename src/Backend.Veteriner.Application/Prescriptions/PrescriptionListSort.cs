using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.Prescriptions;

public enum PrescriptionListSortField
{
    PrescribedAtUtc
}

/// <summary>Reçete listesi sıralama beyaz listesi. <c>sort</c> boşsa varsayılan: <c>PrescribedAtUtc</c> azalan, sonra <c>Id</c> azalan.</summary>
public static class PrescriptionListSort
{
    public static readonly ListSortCatalog<PrescriptionListSortField> Catalog = new(
        ("prescribedAtUtc", PrescriptionListSortField.PrescribedAtUtc));
}
