using Backend.Veteriner.Application.Common;

namespace Backend.Veteriner.Application.LabResults;

public enum LabResultListSortField
{
    RequestedAtUtc
}

/// <summary>Laboratuvar listesi sıralama beyaz listesi. <c>sort</c> boşsa varsayılan: <c>RequestedAtUtc</c> azalan, sonra <c>Id</c> azalan.</summary>
public static class LabResultListSort
{
    public static readonly ListSortCatalog<LabResultListSortField> Catalog = new(
        ("requestedAtUtc", LabResultListSortField.RequestedAtUtc));
}
