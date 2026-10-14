using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Registers;

namespace Backend.Veteriner.Application.Reports.DailyTransactions;

/// <summary>Günlük işlem dökümü dosya sütunlarının tek tanımı (CSV ve XLSX bunu okur) ve Türkçe etiketler.</summary>
internal static class DailyTransactionsReportColumns
{
    public static readonly RegisterExportSpec<DailyTransactionItemDto> Export = new(
    [
        RegisterColumn<DailyTransactionItemDto>.Time(null, "Tarih Saat", r => r.OccurredAtUtc),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Tür", r => KindLabel(r.Kind)),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Müşteri", r => r.ClientName),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Hayvan", r => r.PetName),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Açıklama", r => r.Description),
        RegisterColumn<DailyTransactionItemDto>.Money(null, "Tutar", r => r.Amount),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Ödeme Türü", r => r.PaymentMethod is { } m ? PaymentMethodTurkishDisplay.ToLabel(m) : null),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Kaynak", r => r.IsDirectSale ? "Doğrudan satış" : null),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Durum", r => r.IsVoided ? "İptal edildi" : "Geçerli"),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "Gerekçe", r => r.Reason),
        RegisterColumn<DailyTransactionItemDto>.Text(null, "İşlemi Yapan", r => r.PerformedByName),
    ],
    "Islem Dokumu",
    "gunluk-islem-dokumu");

    public static string KindLabel(DailyTransactionKind kind)
        => kind switch
        {
            DailyTransactionKind.Charge => "Ücret",
            DailyTransactionKind.ChargeVoid => "Ücret iptali",
            DailyTransactionKind.ChargeCorrection => "Ücret düzeltmesi",
            DailyTransactionKind.Payment => "Tahsilat",
            DailyTransactionKind.PaymentVoid => "Tahsilat iptali",
            DailyTransactionKind.PaymentCorrection => "Tahsilat düzeltmesi",
            _ => kind.ToString(),
        };
}
