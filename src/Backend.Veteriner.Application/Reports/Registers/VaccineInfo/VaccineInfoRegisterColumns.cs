namespace Backend.Veteriner.Application.Reports.Registers.VaccineInfo;

/// <summary>
/// Aşı bilgi raporu sütunlarının tek tanımı (CSV ve XLSX bunu okur). Resmî biçim doğrulandığında yalnız bu liste güncellenir.
/// </summary>
internal static class VaccineInfoRegisterColumns
{
    public static readonly IReadOnlyList<RegisterColumn<VaccineInfoRegisterItemDto>> All =
    [
        RegisterColumn<VaccineInfoRegisterItemDto>.Number(null, "#", r => r.RowNumber),
        RegisterColumn<VaccineInfoRegisterItemDto>.Date(null, "Tarih", r => r.AdministeredAtUtc),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "Hasta Sahibi", r => r.OwnerName),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "T.C.", r => r.ClientIdentityNumber),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "Hasta", r => r.PetName),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "Tür", r => r.Species),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "Çip No", r => r.MicrochipNumber),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "Ürün", FormatProduct),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "Seri No", r => r.LotNumber),
        RegisterColumn<VaccineInfoRegisterItemDto>.CalendarDate(null, "S.K.T", r => r.ExpiryDate),
        RegisterColumn<VaccineInfoRegisterItemDto>.Text(null, "Birim", r => r.Unit),
        RegisterColumn<VaccineInfoRegisterItemDto>.Number(null, "Miktar", r => r.Quantity),
        RegisterColumn<VaccineInfoRegisterItemDto>.Number(null, "Fiyat", r => r.UnitPrice),
        RegisterColumn<VaccineInfoRegisterItemDto>.Number(null, "Toplam", r => r.TotalPrice),
    ];

    public static readonly RegisterExportSpec<VaccineInfoRegisterItemDto> Export = new(All, "Aşı Bilgi Raporu", "asi-bilgi-raporu", "Aşı Bilgi Raporu");

    /// <summary>Ürün adı + sonradan eklendi + iptal işaretleri (sözleşme bölüm 16).</summary>
    private static string FormatProduct(VaccineInfoRegisterItemDto r)
        => RegisterDisplay.WithVoidMark(
            r.AddedAfterFinalization ? $"{r.ProductName} (sonradan eklendi)" : r.ProductName,
            r.IsVoided);
}
