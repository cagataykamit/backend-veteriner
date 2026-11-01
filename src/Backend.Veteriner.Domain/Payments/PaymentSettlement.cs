using Backend.Veteriner.Domain.ClientCredits;

namespace Backend.Veteriner.Domain.Payments;

/// <summary><see cref="Payment.Settle"/> sonucu: ücret tahsisleri ve varsa ödemeden doğan müşteri kredisi (FIN-005).</summary>
public sealed record PaymentSettlement(IReadOnlyList<PaymentAllocation> Allocations, ClientCredit? Credit);
