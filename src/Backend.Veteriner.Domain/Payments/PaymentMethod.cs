namespace Backend.Veteriner.Domain.Payments;

/// <summary>
/// Tahsilat kanalı; fatura/muhasebe ayrı kavramlardır.
/// </summary>
public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Transfer = 2
}
