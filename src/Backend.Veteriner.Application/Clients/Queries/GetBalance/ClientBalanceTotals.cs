using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Clients.Queries.GetBalance;

/// <summary>
/// Müşteri bakiyesi toplamları — tek kural: ücret toplamı, ücretlere tahsis edilen ödeme ve kalan (ücret − tahsis);
/// bağlanmamış ödeme yalnız TRY kayıtlardan toplanır ve kalandan düşülmez. Klinik paneli bakiyesi ve portal bakiyesi
/// (ADR-031) bunu kullanır; iptal edilmiş ücret/ödeme sorgu filtresiyle zaten dışarıdadır.
/// </summary>
public sealed record ClientBalanceTotals(
    decimal ChargedAmount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    decimal UnallocatedPaymentsAmount,
    int UnallocatedPaymentsCount)
{
    public static ClientBalanceTotals From(IReadOnlyCollection<Charge> charges, IReadOnlyCollection<Payment> unallocatedPayments)
    {
        var charged = charges.Sum(c => c.Amount);
        var allocated = charges.Sum(c => c.AllocatedAmount);
        var tryPayments = unallocatedPayments
            .Where(p => string.Equals(p.Currency, Charge.DefaultCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new ClientBalanceTotals(
            charged,
            allocated,
            charged - allocated,
            tryPayments.Sum(p => p.UnallocatedAmount),
            tryPayments.Count);
    }
}
