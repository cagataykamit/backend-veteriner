using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.Treatments;

public enum TreatmentListSortField
{
    TreatmentDateUtc
}

/// <summary>Tedavi listesi sıralama beyaz listesi. <c>sort</c> boşsa varsayılan: <c>TreatmentDateUtc</c> azalan, sonra <c>Id</c> azalan.</summary>
public static class TreatmentListSort
{
    public static readonly ListSortCatalog<TreatmentListSortField> Catalog = new(
        ("treatmentDateUtc", TreatmentListSortField.TreatmentDateUtc));
}
