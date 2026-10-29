using Backend.Veteriner.Application.Reports.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Registers;

namespace Backend.Veteriner.Application.Reports.Payments;

/// <summary>Ödeme raporu PDF başlığı ve sütunları (XLSX ile aynı sütunlar ve sıra; tarih İstanbul).</summary>
internal static class PaymentsPdfColumns
{
    public const string Title = "Ödeme Raporu";

    public static readonly IReadOnlyList<RegisterColumn<PaymentReportItemDto>> All =
    [
        RegisterColumn<PaymentReportItemDto>.Time(null, "Tarih", r => r.PaidAtUtc),
        RegisterColumn<PaymentReportItemDto>.Text(null, "Klinik", r => r.ClinicName),
        RegisterColumn<PaymentReportItemDto>.Text(null, "Müşteri", r => r.ClientName),
        RegisterColumn<PaymentReportItemDto>.Text(null, "Hayvan", r => r.PetName),
        RegisterColumn<PaymentReportItemDto>.Money(null, "Tutar", r => r.Amount),
        RegisterColumn<PaymentReportItemDto>.Text(null, "Para Birimi", r => r.Currency),
        RegisterColumn<PaymentReportItemDto>.Text(null, "Ödeme Yöntemi", r => PaymentMethodTurkishDisplay.ToLabel(r.Method)),
        RegisterColumn<PaymentReportItemDto>.Text(null, "Not", r => r.Notes),
    ];
}
