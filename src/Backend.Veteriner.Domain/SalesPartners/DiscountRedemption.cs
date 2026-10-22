using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.SalesPartners;

/// <summary>
/// Bir kiracının indirim kodu kullanımı (ADR-037). Kiracı başına tek kayıt (tek kod). Yüzdeler kullanım anında koddan kopyalanır.
/// İndirim ve komisyonun ne zaman uygulanacağı kuralı yalnız burada yaşar:
/// <list type="bullet">
/// <item>12 ay penceresi ilk gerçek tahsilatla başlar (deneme süresi sayılmaz).</item>
/// <item>Aylık: fiyatlandırma anı pencere içindeyse indirim ve komisyon (aylık yenilemede en çok 12 tahsilat).</item>
/// <item>Yıllık: yalnız etkin tahsilatı olmayan kullanımın tek tahsilatı (12 ayı kapsar); komisyon tek seferlik.</item>
/// <item>Komisyon yalnız gerçek tahsilatta, net (KDV hariç, indirim sonrası) tutar üzerinden; iade/iptalde geri alınır.</item>
/// </list>
/// </summary>
public sealed class DiscountRedemption : AggregateRoot
{
    public const int WindowMonths = 12;

    public static readonly Error InvalidCode = new("DiscountCodes.Invalid", "Ortak kodu geçersiz.");

    private readonly List<DiscountRedemptionCollection> _collections = [];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid DiscountCodeId { get; private set; }
    public Guid SalesPartnerId { get; private set; }
    public string Code { get; private set; } = default!;
    public decimal DiscountPercent { get; private set; }
    public decimal CommissionPercent { get; private set; }
    public DateTime RedeemedAtUtc { get; private set; }
    public DateTime? WindowStartsAtUtc { get; private set; }
    public DateTime? WindowEndsAtUtc { get; private set; }

    public IReadOnlyCollection<DiscountRedemptionCollection> Collections => _collections.AsReadOnly();

    private DiscountRedemption() { }

    /// <summary>
    /// Kodu kiracı için kullanır. Kod kullanılamıyorsa ya da kullanan ortağın kendisiyse tek tip <see cref="InvalidCode"/> döner
    /// (numaralandırma ve ortak kimliği sızmasın). Kiracının zaten kodu olup olmadığı kalıcılık katmanında (benzersiz kiracı) denetlenir.
    /// </summary>
    public static Result<DiscountRedemption> Redeem(
        Guid tenantId,
        string? redeemerEmail,
        DiscountCode code,
        SalesPartner partner,
        int redemptionCount,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (code.SalesPartnerId != partner.Id)
            throw new ArgumentException("Kod bu ortağa ait değil.", nameof(partner));

        if (!code.IsRedeemable(partner.IsActive, redemptionCount, utcNow) || partner.IsSelf(tenantId, redeemerEmail))
            return Result<DiscountRedemption>.Failure(InvalidCode);

        return Result<DiscountRedemption>.Success(new DiscountRedemption
        {
            TenantId = tenantId,
            DiscountCodeId = code.Id,
            SalesPartnerId = partner.Id,
            Code = code.Code,
            DiscountPercent = code.DiscountPercent,
            CommissionPercent = code.CommissionPercent,
            RedeemedAtUtc = DiscountTime.ToUtc(utcNow),
        });
    }

    /// <summary>Etkin (iptal edilmemiş) tahsilatı var mı: müşteri "ücretli" sayılır.</summary>
    public bool HasActiveCollection => _collections.Any(c => c.IsActive);

    /// <summary>Verilen fiyatlandırma anında ve dönemde indirim/komisyon uygulanır mı.</summary>
    public bool IsEligible(DateTime pricedAtUtc, BillingPeriodKind period)
    {
        var at = DiscountTime.ToUtc(pricedAtUtc);
        var withinWindow = WindowEndsAtUtc is not { } end || at < end;

        return period switch
        {
            BillingPeriodKind.Monthly => withinWindow,
            BillingPeriodKind.Yearly => withinWindow && !HasActiveCollection,
            _ => false,
        };
    }

    /// <summary>Brüt tutara uygulanacak indirim (kuruş); uygun değilse 0. Çekilecek tutar en az 1 kuruş kalır.</summary>
    public long CalculateDiscountMinor(long grossAmountMinor, DateTime pricedAtUtc, BillingPeriodKind period)
    {
        if (grossAmountMinor <= 0 || !IsEligible(pricedAtUtc, period))
            return 0;

        var discount = PercentOf(grossAmountMinor, DiscountPercent);
        return Math.Min(discount, grossAmountMinor - 1);
    }

    /// <summary>
    /// Gerçek tahsilatı (ödeme sağlayıcısının doğruladığı) kaydeder ve komisyonu hesaplar. Uygun değilse ya da aynı checkout
    /// oturumu zaten yazılmışsa <c>null</c> (idempotent). İlk kayıt 12 ay penceresini başlatır.
    /// </summary>
    public DiscountRedemptionCollection? RecordCollection(
        Guid checkoutSessionId,
        BillingPeriodKind period,
        long grossAmountMinor,
        long discountAmountMinor,
        DateTime pricedAtUtc,
        DateTime collectedAtUtc)
    {
        if (checkoutSessionId == Guid.Empty)
            throw new ArgumentException("CheckoutSessionId geçersiz.", nameof(checkoutSessionId));
        if (grossAmountMinor <= 0)
            throw new ArgumentOutOfRangeException(nameof(grossAmountMinor), "Brüt tutar pozitif olmalı.");
        if (discountAmountMinor < 0 || discountAmountMinor >= grossAmountMinor)
            throw new ArgumentOutOfRangeException(nameof(discountAmountMinor), "İndirim 0 ile brüt tutar arasında olmalı.");

        if (_collections.Any(c => c.CheckoutSessionId == checkoutSessionId) || !IsEligible(pricedAtUtc, period))
            return null;

        var collectedAt = DiscountTime.ToUtc(collectedAtUtc);
        if (WindowStartsAtUtc is null)
        {
            WindowStartsAtUtc = collectedAt;
            WindowEndsAtUtc = collectedAt.AddMonths(WindowMonths);
        }

        var net = grossAmountMinor - discountAmountMinor;
        var collection = new DiscountRedemptionCollection(
            Id,
            checkoutSessionId,
            period,
            collectedAt,
            grossAmountMinor,
            discountAmountMinor,
            PercentOf(net, CommissionPercent));

        _collections.Add(collection);
        return collection;
    }

    /// <summary>Tahsilat iade/iptal edildi: komisyon hak edişten düşer; ödenmişse geri alınacak olarak kalır.</summary>
    public Result<DiscountRedemptionCollection> CancelCollection(Guid collectionId, string reason, DateTime utcNow)
    {
        var collection = Find(collectionId);
        if (collection is null)
            return Result<DiscountRedemptionCollection>.Failure(CollectionNotFound);

        var result = collection.Cancel(utcNow, reason);
        return result.IsSuccess
            ? Result<DiscountRedemptionCollection>.Success(collection)
            : Result<DiscountRedemptionCollection>.Failure(result.Error);
    }

    public Result MarkCommissionPaid(Guid collectionId, DateTime paidAtUtc, string? note)
    {
        var collection = Find(collectionId);
        return collection is null ? Result.Failure(CollectionNotFound) : collection.MarkPaid(paidAtUtc, note);
    }

    public static readonly Error CollectionNotFound = new("DiscountRedemptions.CollectionNotFound", "Tahsilat satırı bulunamadı.");

    private DiscountRedemptionCollection? Find(Guid collectionId) => _collections.FirstOrDefault(c => c.Id == collectionId);

    private static long PercentOf(long amountMinor, decimal percent)
        => (long)Math.Round(amountMinor * percent / 100m, MidpointRounding.AwayFromZero);
}
