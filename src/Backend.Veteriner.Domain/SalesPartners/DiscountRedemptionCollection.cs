using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.SalesPartners;

/// <summary>
/// Kod kullanan kiracının bir gerçek tahsilatı ve ondan doğan komisyon (ADR-037 madde 4). Tutarlar kuruş; net = brüt − indirim
/// (KDV hariç), komisyon net üzerinden. Durum geçişleri yalnız <see cref="DiscountRedemption"/> üzerinden yapılır.
/// </summary>
public sealed class DiscountRedemptionCollection
{
    public const int MaxNoteLength = 500;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid DiscountRedemptionId { get; private set; }
    public Guid CheckoutSessionId { get; private set; }
    public BillingPeriodKind BillingPeriod { get; private set; }
    public DateTime CollectedAtUtc { get; private set; }
    public long GrossAmountMinor { get; private set; }
    public long DiscountAmountMinor { get; private set; }
    public long NetAmountMinor { get; private set; }
    public long CommissionAmountMinor { get; private set; }
    public CommissionStatus Status { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public string? PaidNote { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }

    private DiscountRedemptionCollection() { }

    internal DiscountRedemptionCollection(
        Guid discountRedemptionId,
        Guid checkoutSessionId,
        BillingPeriodKind billingPeriod,
        DateTime collectedAtUtc,
        long grossAmountMinor,
        long discountAmountMinor,
        long commissionAmountMinor)
    {
        DiscountRedemptionId = discountRedemptionId;
        CheckoutSessionId = checkoutSessionId;
        BillingPeriod = billingPeriod;
        CollectedAtUtc = collectedAtUtc;
        GrossAmountMinor = grossAmountMinor;
        DiscountAmountMinor = discountAmountMinor;
        NetAmountMinor = grossAmountMinor - discountAmountMinor;
        CommissionAmountMinor = commissionAmountMinor;
        Status = CommissionStatus.Accrued;
    }

    /// <summary>Tahsilat etkin mi (iptal/iade edilmemiş): ciro ve hak edişe girer.</summary>
    public bool IsActive => Status != CommissionStatus.Cancelled;

    /// <summary>Komisyonu ödendikten sonra iptal edilen satır: komisyon ortaktan geri alınacak.</summary>
    public bool IsClawback => Status == CommissionStatus.Cancelled && PaidAtUtc.HasValue;

    internal Result MarkPaid(DateTime paidAtUtc, string? note)
    {
        if (Status != CommissionStatus.Accrued)
            return Result.Failure("DiscountRedemptions.CommissionNotAccrued", "Yalnız tahakkuk etmiş (ödenmemiş, iptal edilmemiş) komisyon ödendi işaretlenebilir.");

        Status = CommissionStatus.Paid;
        PaidAtUtc = DiscountTime.ToUtc(paidAtUtc);
        PaidNote = NormalizeNote(note);
        return Result.Success();
    }

    internal Result Cancel(DateTime utcNow, string reason)
    {
        if (Status == CommissionStatus.Cancelled)
            return Result.Failure("DiscountRedemptions.CollectionAlreadyCancelled", "Tahsilat zaten iptal edilmiş.");

        var trimmed = NormalizeNote(reason);
        if (trimmed is null)
            return Result.Failure("DiscountRedemptions.Validation", "İptal gerekçesi zorunludur.");

        Status = CommissionStatus.Cancelled;
        CancelledAtUtc = DiscountTime.ToUtc(utcNow);
        CancelReason = trimmed;
        return Result.Success();
    }

    private static string? NormalizeNote(string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return trimmed is { Length: > MaxNoteLength } ? trimmed[..MaxNoteLength] : trimmed;
    }
}
