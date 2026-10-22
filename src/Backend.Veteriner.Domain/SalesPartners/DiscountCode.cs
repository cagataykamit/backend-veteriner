using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.SalesPartners;

/// <summary>
/// Satış ortağının indirim kodu (ADR-037 madde 1-2): müşteriye indirim yüzdesi, ortağa komisyon yüzdesi kod başına girilir
/// (varsayılan yok). Geçerlilik penceresi ve kullanım limiti isteğe bağlıdır. Kod metni oluşturulduktan sonra değişmez;
/// yüzde değişikliği var olan kullanımları etkilemez (kullanım, yüzdeleri kullanım anında kopyalar).
/// </summary>
public sealed class DiscountCode : AggregateRoot
{
    /// <summary>İndirim tam yüzde olamaz (çekilecek tutar sıfıra inmesin).</summary>
    public const decimal MaxDiscountPercentExclusive = 100m;

    public const decimal MaxCommissionPercent = 100m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SalesPartnerId { get; private set; }
    public string Code { get; private set; } = default!;
    public string NormalizedCode { get; private set; } = default!;
    public decimal DiscountPercent { get; private set; }
    public decimal CommissionPercent { get; private set; }
    public DateTime? ValidFromUtc { get; private set; }
    public DateTime? ValidUntilUtc { get; private set; }
    public int? MaxRedemptions { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }

    private DiscountCode() { }

    public static Result<DiscountCode> Create(
        Guid salesPartnerId,
        string code,
        decimal discountPercent,
        decimal commissionPercent,
        DateTime? validFromUtc,
        DateTime? validUntilUtc,
        int? maxRedemptions,
        DateTime utcNow)
    {
        if (salesPartnerId == Guid.Empty)
            throw new ArgumentException("SalesPartnerId geçersiz.", nameof(salesPartnerId));

        var normalized = DiscountCodeText.Normalize(code);
        if (normalized is null)
        {
            return Result<DiscountCode>.Failure(
                "DiscountCodes.Validation",
                $"Kod {DiscountCodeText.MinLength}-{DiscountCodeText.MaxLength} karakter olmalı; harf, rakam ve tire içerebilir.");
        }

        var discountCode = new DiscountCode
        {
            SalesPartnerId = salesPartnerId,
            Code = normalized,
            NormalizedCode = normalized,
            IsActive = true,
            CreatedAtUtc = DiscountTime.ToUtc(utcNow),
        };

        var terms = discountCode.ApplyTerms(discountPercent, commissionPercent, validFromUtc, validUntilUtc, maxRedemptions);
        return terms.IsSuccess ? Result<DiscountCode>.Success(discountCode) : Result<DiscountCode>.Failure(terms.Error);
    }

    public Result UpdateTerms(
        decimal discountPercent,
        decimal commissionPercent,
        DateTime? validFromUtc,
        DateTime? validUntilUtc,
        int? maxRedemptions,
        DateTime utcNow)
    {
        var result = ApplyTerms(discountPercent, commissionPercent, validFromUtc, validUntilUtc, maxRedemptions);
        if (result.IsSuccess)
            UpdatedAtUtc = DiscountTime.ToUtc(utcNow);
        return result;
    }

    public void Deactivate(DateTime utcNow)
    {
        if (!IsActive)
            return;

        IsActive = false;
        DeactivatedAtUtc = DiscountTime.ToUtc(utcNow);
        UpdatedAtUtc = DeactivatedAtUtc;
    }

    public void Activate(DateTime utcNow)
    {
        if (IsActive)
            return;

        IsActive = true;
        DeactivatedAtUtc = null;
        UpdatedAtUtc = DiscountTime.ToUtc(utcNow);
    }

    /// <summary>
    /// Kod şu an kullanılabilir mi (ADR-037 madde 5): kod ve ortak aktif, geçerlilik penceresi içinde, limit dolmamış.
    /// Kodun geçerliliği için tek kural budur; doğrulama ucu, kayıt ve abonelik akışı bunu kullanır.
    /// </summary>
    public bool IsRedeemable(bool partnerActive, int redemptionCount, DateTime utcNow)
    {
        var now = DiscountTime.ToUtc(utcNow);
        if (!IsActive || !partnerActive)
            return false;
        if (ValidFromUtc is { } from && now < from)
            return false;
        if (ValidUntilUtc is { } until && now >= until)
            return false;
        return MaxRedemptions is not { } max || redemptionCount < max;
    }

    private Result ApplyTerms(
        decimal discountPercent,
        decimal commissionPercent,
        DateTime? validFromUtc,
        DateTime? validUntilUtc,
        int? maxRedemptions)
    {
        if (discountPercent < 0 || discountPercent >= MaxDiscountPercentExclusive || decimal.Round(discountPercent, 2) != discountPercent)
            return Result.Failure("DiscountCodes.Validation", "İndirim yüzdesi 0 ile 100 arasında (100 hariç), en çok 2 ondalık olmalı.");
        if (commissionPercent < 0 || commissionPercent > MaxCommissionPercent || decimal.Round(commissionPercent, 2) != commissionPercent)
            return Result.Failure("DiscountCodes.Validation", "Komisyon yüzdesi 0 ile 100 arasında, en çok 2 ondalık olmalı.");

        var from = DiscountTime.ToUtc(validFromUtc);
        var until = DiscountTime.ToUtc(validUntilUtc);
        if (from.HasValue && until.HasValue && until.Value <= from.Value)
            return Result.Failure("DiscountCodes.Validation", "Geçerlilik bitişi başlangıçtan sonra olmalı.");
        if (maxRedemptions is < 1)
            return Result.Failure("DiscountCodes.Validation", "Kullanım limiti en az 1 olmalı.");

        DiscountPercent = discountPercent;
        CommissionPercent = commissionPercent;
        ValidFromUtc = from;
        ValidUntilUtc = until;
        MaxRedemptions = maxRedemptions;
        return Result.Success();
    }
}
