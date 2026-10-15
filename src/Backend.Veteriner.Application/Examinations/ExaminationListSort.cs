using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.Examinations;

public enum ExaminationListSortField
{
    ExaminedAtUtc
}

/// <summary>
/// Muayene listesi sıralama beyaz listesi. <c>sort</c> boşsa varsayılan: <c>ExaminedAtUtc</c> azalan, sonra <c>Id</c> azalan.
/// <c>examinationDateUtc</c> mevcut istemciler için <c>examinedAtUtc</c> eş adıdır.
/// </summary>
public static class ExaminationListSort
{
    public static readonly ListSortCatalog<ExaminationListSortField> Catalog = new(
        ("examinedAtUtc", ExaminationListSortField.ExaminedAtUtc),
        ("examinationDateUtc", ExaminationListSortField.ExaminedAtUtc));
}
