using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.Payments;

/// <summary>CSV / operasyonel rapor için <see cref="PaymentMethod"/> Türkçe etiketleri.</summary>
internal static class PaymentMethodTurkishDisplay
{
    public static string ToLabel(PaymentMethod method)
        => method switch
        {
            PaymentMethod.Cash => "Nakit",
            PaymentMethod.Card => "Kart",
            PaymentMethod.Transfer => "Havale-EFT",
            _ => "Diğer"
        };
}
