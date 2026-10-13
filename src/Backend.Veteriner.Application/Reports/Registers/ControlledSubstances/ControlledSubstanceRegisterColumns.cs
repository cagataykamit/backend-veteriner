namespace Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;

/// <summary>
/// Narkotik ve psikotropik ürünler stok ve sarf defteri sütunlarının tek tanımı (CSV ve XLSX bunu okur).
/// Ayrı kalan/bakiye sütunu yoktur. Resmî biçim doğrulandığında yalnız bu liste güncellenir.
/// </summary>
internal static class ControlledSubstanceRegisterColumns
{
    private const string Received = "SATIN/İADE/DEVİR ALINAN ÜRÜNÜN";
    private const string Issued = "SATILAN/İADE VEYA DEVREDİLEN ÜRÜNÜN";

    public static readonly IReadOnlyList<RegisterColumn<ControlledSubstanceRegisterItemDto>> All =
    [
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Number(null, "Sıra No", r => r.RowNumber),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Text(null, "Ürün Adı, Ticari Adı ve Farmakolojik Şekli", FormatProduct),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Text(null, "Ticari Takdim Şekli", r => r.CommercialPresentation),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Number(null, "Miktar", r => r.Quantity),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Text(null, "Seri No", r => r.LotNumber),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Text(Received, "Alındığı Yer", r => r.ReceivedFrom),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.CalendarDate(Received, "Tarih", r => r.ReceivedDate),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Text(Received, "Belge No", r => r.ReceivedDocumentNumber),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Text(Issued, "Çıkış Yapılan Yer", r => r.IssuedTo),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.CalendarDate(Issued, "Tarih", r => r.IssuedDate),
        RegisterColumn<ControlledSubstanceRegisterItemDto>.Text(Issued, "Reçete/Belge No", r => r.IssuedDocumentNumber),
    ];

    public static readonly RegisterExportSpec<ControlledSubstanceRegisterItemDto> Export =
        new(All, "Narkotik-Psikotropik", "narkotik-psikotropik-defteri");

    /// <summary><c>Ad (farmakolojik şekil)</c>; şekil yoksa yalnız ad.</summary>
    private static string FormatProduct(ControlledSubstanceRegisterItemDto r)
        => string.IsNullOrWhiteSpace(r.PharmacologicalForm) ? r.ProductName : $"{r.ProductName} ({r.PharmacologicalForm})";
}
