namespace Backend.Veteriner.Application.Reports.Registers.Vaccines;

/// <summary>
/// Aşı kayıt defteri sütunlarının tek tanımı (CSV ve XLSX bunu okur). Resmî biçim doğrulandığında yalnız bu liste güncellenir.
/// </summary>
internal static class VaccineRegisterColumns
{
    public static readonly IReadOnlyList<RegisterColumn<VaccineRegisterItemDto>> All =
    [
        RegisterColumn<VaccineRegisterItemDto>.Number(null, "Sıra No", r => r.RowNumber),
        RegisterColumn<VaccineRegisterItemDto>.Text(null, "Ticari Adı", r => RegisterDisplay.WithVoidMark(r.TradeName, r.IsReversed)),
        RegisterColumn<VaccineRegisterItemDto>.Text(null, "Seri No", r => r.LotNumber),
        RegisterColumn<VaccineRegisterItemDto>.CalendarDate(null, "Son Kullanma Tarihi", r => r.ExpiryDate),
        RegisterColumn<VaccineRegisterItemDto>.Number(null, "Toplam Miktar (Doz)", r => r.TotalQuantity),
        RegisterColumn<VaccineRegisterItemDto>.Text(null, "Satın Alınan Firma", r => r.SupplierName),
        RegisterColumn<VaccineRegisterItemDto>.Text(null, "Nakil Şekli (Soğuk Zincir)", r => r.TransportMethod),
        RegisterColumn<VaccineRegisterItemDto>.Text(null, "Nakilde Kullanılan İzleme Cihazı (Soğuk Zincir İndikatörü)", r => r.ColdChainMonitor),
        RegisterColumn<VaccineRegisterItemDto>.CalendarDate(null, "Depo Çıkış Tarihi", r => r.DispatchedDate),
        RegisterColumn<VaccineRegisterItemDto>.CalendarDate(null, "Teslim Tarihi", r => r.DeliveredDate),
        RegisterColumn<VaccineRegisterItemDto>.CalendarDate(null, "Fatura Tarihi", r => r.InvoiceDate),
        RegisterColumn<VaccineRegisterItemDto>.Text(null, "Fatura No", r => r.InvoiceNumber),
    ];

    public static readonly RegisterExportSpec<VaccineRegisterItemDto> Export = new(All, "Aşı Kayıt Defteri", "asi-kayit-defteri");
}
