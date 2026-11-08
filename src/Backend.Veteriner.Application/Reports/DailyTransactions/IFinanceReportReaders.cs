using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.DailyTransactions;

/// <summary>
/// Doğrulanmış ve klinik kapsamı çözülmüş mali olay süzgeci. <c>ClinicId</c> tek klinik; null ise <c>AccessibleClinicIds</c> (null = kiracı geneli).
/// Pencere <c>[FromUtc, ToUtcExclusive)</c> (İstanbul gün sınırı).
/// </summary>
public sealed record FinanceEventFilter(
    Guid TenantId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    DailyTransactionKind? Kind,
    PaymentMethod? PaymentMethod,
    Guid? PerformedByUserId)
{
    /// <summary>Doluysa yalnız bu kasanın nakit olayları (TIER-008); boşsa kasa süzülmez.</summary>
    public CashRegisterScope? CashRegister { get; init; }
}

/// <summary>
/// Kasa süzgeci: yalnız nakit olaylar. <c>CashRegisterId</c> kasası olanlar; <c>IncludesUnassigned</c> ise kasası boş nakit olaylar da
/// (kasası boş nakit Ana kasanındır; tek kural). Ana kasası henüz olmayan klinikte <c>CashRegisterId</c> boş, yalnız kasasızlar gelir.
/// </summary>
public sealed record CashRegisterScope(Guid? CashRegisterId, bool IncludesUnassigned);

public sealed record DailyTransactionsReadResult(
    int TotalCount,
    DailyTransactionTotalsDto Totals,
    IReadOnlyList<DailyTransactionItemDto> Items);

/// <summary>Günlük işlem dökümü okuyucusu (Command DB). JSON ve dışa aktarma aynı çağrıyı kullanır.</summary>
public interface IDailyTransactionsReader
{
    Task<DailyTransactionsReadResult> ReadAsync(FinanceEventFilter filter, int skip, int take, CancellationToken ct = default);
}

/// <summary>Kasa özetinin ham girdisi: aralıktaki tahsilat ve tahsilat iptali olayları (ad çözümlemesi yapılmış).</summary>
/// <param name="Voided">True: tahsilat iptali olayı (<c>OccurredAtUtc</c> iptal zamanı); false: tahsilat olayı (ödeme zamanı).</param>
/// <param name="IsCreditRefund">Müşteri kredisi iadesi (FIN-005 K6): kasadan çıkış; <c>PaymentId</c> kredi kimliği, <c>Voided</c> false.</param>
public sealed record CashEventRow(
    bool Voided,
    DateTime OccurredAtUtc,
    Guid PaymentId,
    DateTime PaidAtUtc,
    Guid ClientId,
    string ClientName,
    decimal Amount,
    PaymentMethod PaymentMethod,
    bool IsDirectSale,
    Guid? CollectedByUserId,
    string? CollectedByName,
    Guid? PerformedByUserId,
    string? PerformedByName,
    string? Reason,
    bool IsCreditRefund = false,
    Guid? CashRegisterId = null);

/// <summary>Tahsilat olaylarının toplamları (kasa devreden bakiyesi; satır yüklemeden SQL'de toplanır).</summary>
public sealed record CashEventTotals(decimal CollectedAmount, decimal VoidedAmount, decimal RefundedCreditAmount)
{
    /// <summary>Kasa özetiyle aynı net: tahsilat − iptal − kredi iadesi.</summary>
    public decimal NetAmount => CollectedAmount - VoidedAmount - RefundedCreditAmount;
}

/// <summary>Kasa özeti okuyucusu: dökümle aynı mali olay kaynağından en çok <c>maxRows + 1</c> tahsilat olayı döner (aşım tespiti için).</summary>
public interface ICashEventsReader
{
    Task<IReadOnlyList<CashEventRow>> ReadAsync(FinanceEventFilter filter, int maxRows, CancellationToken ct = default);

    /// <summary>Süzgeçteki tahsilat, iptal ve kredi iadesi toplamları (satır sınırı yok).</summary>
    Task<CashEventTotals> SumAsync(FinanceEventFilter filter, CancellationToken ct = default);
}
