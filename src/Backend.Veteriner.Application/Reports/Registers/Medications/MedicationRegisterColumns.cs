namespace Backend.Veteriner.Application.Reports.Registers.Medications;

/// <summary>
/// İlaç kayıt defteri sütunlarının tek tanımı (CSV ve XLSX bunu okur). Resmî biçim doğrulandığında yalnız bu liste güncellenir.
/// </summary>
internal static class MedicationRegisterColumns
{
    public static readonly IReadOnlyList<RegisterColumn<MedicationRegisterItemDto>> All =
    [
        RegisterColumn<MedicationRegisterItemDto>.Number(null, "Sıra No", r => r.RowNumber),
        RegisterColumn<MedicationRegisterItemDto>.Text(null, "Ürün", r => RegisterDisplay.WithVoidMark(r.ProductName, r.IsReversed)),
        RegisterColumn<MedicationRegisterItemDto>.Text(null, "Farmakolojik Şekli", r => r.PharmacologicalForm),
        RegisterColumn<MedicationRegisterItemDto>.Text(null, "Takdim Şekli", r => r.PresentationForm),
        RegisterColumn<MedicationRegisterItemDto>.Number(null, "Adet", r => r.Quantity),
        RegisterColumn<MedicationRegisterItemDto>.Text(null, "Birim", r => r.Unit),
        RegisterColumn<MedicationRegisterItemDto>.Text(null, "Firma Adı", r => r.SupplierName),
        RegisterColumn<MedicationRegisterItemDto>.CalendarDate(null, "Geldiği Tarih", r => r.ReceivedDate),
        RegisterColumn<MedicationRegisterItemDto>.CalendarDate(null, "Fatura Tarihi", r => r.InvoiceDate),
        RegisterColumn<MedicationRegisterItemDto>.Text(null, "Fatura No", r => r.InvoiceNumber),
        RegisterColumn<MedicationRegisterItemDto>.Text(null, "Seri No", r => r.LotNumber),
    ];

    public static readonly RegisterExportSpec<MedicationRegisterItemDto> Export = new(All, "İlaç Kayıt Defteri", "ilac-kayit-defteri");
}
