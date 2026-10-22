using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.Imaging;

public enum ImagingRecordListSortField
{
    PerformedAtUtc
}

/// <summary>Görüntüleme listesi sıralama beyaz listesi. <c>sort</c> boşsa varsayılan: <c>PerformedAtUtc</c> azalan, sonra <c>Id</c> azalan.</summary>
public static class ImagingRecordListSort
{
    public static readonly ListSortCatalog<ImagingRecordListSortField> Catalog = new(
        ("performedAtUtc", ImagingRecordListSortField.PerformedAtUtc));
}
