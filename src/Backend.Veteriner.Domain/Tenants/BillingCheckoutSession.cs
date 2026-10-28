using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Tenants;

public sealed class BillingCheckoutSession : AggregateRoot, ITenantOwned
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public SubscriptionPlanCode CurrentPlanCode { get; private set; }
    public SubscriptionPlanCode TargetPlanCode { get; private set; }

    /// <summary>Satın alınan dönem (BILL-001); etkinleştirme ve satış ortağı tahsilatı bu dönemle yapılır.</summary>
    public BillingPeriodKind BillingPeriod { get; private set; } = BillingPeriodKind.Monthly;

    public BillingCheckoutSessionStatus Status { get; private set; }
    public BillingProvider Provider { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? FailedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>Satış ortağı kodu indirimi öncesi tahsilat tutarı (kuruş); fiyat kaydı olmayan eski oturumlarda <c>null</c>.</summary>
    public long? GrossAmountMinor { get; private set; }

    /// <summary>Uygulanan satış ortağı kodu indirimi (kuruş); çekilen tutar = brüt − indirim (ADR-037).</summary>
    public long? DiscountAmountMinor { get; private set; }

    /// <summary>Çift ödeme iadesinin durumu (BILL-005); olağan oturumda <see cref="BillingCheckoutRefundStatus.None"/>.</summary>
    public BillingCheckoutRefundStatus RefundStatus { get; private set; }

    public DateTime? DuplicatePaymentDetectedAtUtc { get; private set; }
    public DateTime? RefundedAtUtc { get; private set; }
    public int RefundAttemptCount { get; private set; }
    public string? RefundFailureReason { get; private set; }

    public const int RefundFailureReasonMaxLength = 500;

    private BillingCheckoutSession() { }

    public static BillingCheckoutSession CreatePending(
        Guid tenantId,
        SubscriptionPlanCode currentPlanCode,
        SubscriptionPlanCode targetPlanCode,
        BillingProvider provider,
        DateTime utcNow,
        DateTime? expiresAtUtc,
        BillingPeriodKind billingPeriod = BillingPeriodKind.Monthly)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (!Enum.IsDefined(billingPeriod))
            throw new ArgumentOutOfRangeException(nameof(billingPeriod));

        var now = NormalizeUtc(utcNow);
        return new BillingCheckoutSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CurrentPlanCode = currentPlanCode,
            TargetPlanCode = targetPlanCode,
            BillingPeriod = billingPeriod,
            Status = BillingCheckoutSessionStatus.Pending,
            Provider = provider,
            ExpiresAtUtc = expiresAtUtc.HasValue ? NormalizeUtc(expiresAtUtc.Value) : null,
            CreatedAtUtc = now,
            UpdatedAtUtc = null,
        };
    }

    /// <summary>Checkout başlarken hesaplanan tutar; tahsilat olayında komisyon bu kayıttan hesaplanır.</summary>
    public void RecordPricing(long grossAmountMinor, long discountAmountMinor)
    {
        if (grossAmountMinor <= 0)
            throw new ArgumentOutOfRangeException(nameof(grossAmountMinor), "Brüt tutar pozitif olmalı.");
        if (discountAmountMinor < 0 || discountAmountMinor >= grossAmountMinor)
            throw new ArgumentOutOfRangeException(nameof(discountAmountMinor), "İndirim 0 ile brüt tutar arasında olmalı.");

        GrossAmountMinor = grossAmountMinor;
        DiscountAmountMinor = discountAmountMinor;
    }

    public bool IsOpen(DateTime utcNow)
    {
        var now = NormalizeUtc(utcNow);
        if (ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= now)
            return false;

        return Status is BillingCheckoutSessionStatus.Pending or BillingCheckoutSessionStatus.RedirectReady;
    }

    public void SetRedirectReady(string? checkoutUrl, string? externalReference, DateTime utcNow)
    {
        EnsureMutableOpen();
        CheckoutUrl = string.IsNullOrWhiteSpace(checkoutUrl) ? null : checkoutUrl.Trim();
        ExternalReference = string.IsNullOrWhiteSpace(externalReference) ? null : externalReference.Trim();
        Status = BillingCheckoutSessionStatus.RedirectReady;
        UpdatedAtUtc = NormalizeUtc(utcNow);
    }

    public void MarkCompleted(DateTime utcNow, string? externalReference = null)
    {
        EnsureMutableOpen();
        Complete(utcNow, externalReference);
    }

    /// <summary>
    /// Sağlayıcının doğruladığı ödeme oturumu kapanmış olsa da (süresi dolmuş, iptal ya da başarısız denemeyle işaretlenmiş)
    /// tamamlar: para alınmıştır, paket açılmalıdır. Yalnız tamamlanmış oturum değiştirilemez.
    /// </summary>
    public void MarkCompletedByVerifiedPayment(DateTime utcNow, string? externalReference = null)
    {
        if (Status == BillingCheckoutSessionStatus.Completed)
            throw new InvalidOperationException("Bu checkout session zaten tamamlanmış.");

        Complete(utcNow, externalReference);
    }

    private void Complete(DateTime utcNow, string? externalReference)
    {
        Status = BillingCheckoutSessionStatus.Completed;
        CompletedAtUtc = NormalizeUtc(utcNow);
        UpdatedAtUtc = CompletedAtUtc;
        if (!string.IsNullOrWhiteSpace(externalReference))
            ExternalReference = externalReference.Trim();
    }

    /// <summary>
    /// Doğrulanmış ödeme, aynı denemede başka bir ödeme paketi zaten açtıktan sonra geldi: paket açılmaz, ödeme iade edilecek.
    /// Açık oturum kapanır; tamamlanmış oturum çift ödeme sayılamaz.
    /// </summary>
    public void MarkDuplicatePayment(DateTime utcNow)
    {
        if (Status == BillingCheckoutSessionStatus.Completed)
            throw new InvalidOperationException("Tamamlanmış checkout session çift ödeme olarak işaretlenemez.");
        if (RefundStatus != BillingCheckoutRefundStatus.None)
            throw new InvalidOperationException("Bu checkout session zaten çift ödeme olarak işaretlenmiş.");

        var now = NormalizeUtc(utcNow);
        if (Status is BillingCheckoutSessionStatus.Pending or BillingCheckoutSessionStatus.RedirectReady)
            Status = BillingCheckoutSessionStatus.Cancelled;
        RefundStatus = BillingCheckoutRefundStatus.Pending;
        DuplicatePaymentDetectedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void RecordRefundSucceeded(DateTime utcNow)
    {
        EnsureRefundPending();
        var now = NormalizeUtc(utcNow);
        RefundAttemptCount++;
        RefundStatus = BillingCheckoutRefundStatus.Refunded;
        RefundedAtUtc = now;
        RefundFailureReason = null;
        UpdatedAtUtc = now;
    }

    /// <param name="requiresManualRefund">Yeniden denenmeyecek: platform yöneticisi sağlayıcı panelinden iade eder.</param>
    public void RecordRefundFailed(DateTime utcNow, string reason, bool requiresManualRefund)
    {
        EnsureRefundPending();
        RefundAttemptCount++;
        var trimmed = reason?.Trim() ?? "";
        RefundFailureReason = trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, RefundFailureReasonMaxLength)];
        if (requiresManualRefund)
            RefundStatus = BillingCheckoutRefundStatus.ManualRequired;
        UpdatedAtUtc = NormalizeUtc(utcNow);
    }

    /// <summary>Platform yöneticisi iadeyi sağlayıcı panelinden yaptı. Tekrarında değişiklik yok.</summary>
    public void MarkRefundedManually(DateTime utcNow)
    {
        if (RefundStatus == BillingCheckoutRefundStatus.Refunded)
            return;
        if (RefundStatus != BillingCheckoutRefundStatus.ManualRequired)
            throw new InvalidOperationException("Yalnız elle iade bekleyen çift ödeme işaretlenebilir.");

        var now = NormalizeUtc(utcNow);
        RefundStatus = BillingCheckoutRefundStatus.Refunded;
        RefundedAtUtc = now;
        UpdatedAtUtc = now;
    }

    private void EnsureRefundPending()
    {
        if (RefundStatus != BillingCheckoutRefundStatus.Pending)
            throw new InvalidOperationException("Bu checkout session için bekleyen iade yok.");
    }

    public void MarkFailed(DateTime utcNow, string? externalReference = null)
    {
        EnsureMutableOpen();
        Status = BillingCheckoutSessionStatus.Failed;
        FailedAtUtc = NormalizeUtc(utcNow);
        UpdatedAtUtc = FailedAtUtc;
        if (!string.IsNullOrWhiteSpace(externalReference))
            ExternalReference = externalReference.Trim();
    }

    /// <summary>Açık ve süresi dolmamış session için başarısızlık işaretler; aksi halde no-op.</summary>
    public void TryMarkFailedIfOpen(DateTime utcNow, string? externalReference = null)
    {
        if (!IsOpen(utcNow))
            return;

        Status = BillingCheckoutSessionStatus.Failed;
        FailedAtUtc = NormalizeUtc(utcNow);
        UpdatedAtUtc = FailedAtUtc;
        if (!string.IsNullOrWhiteSpace(externalReference))
            ExternalReference = externalReference.Trim();
    }

    public void MarkExpired(DateTime utcNow)
    {
        if (Status is BillingCheckoutSessionStatus.Completed
            or BillingCheckoutSessionStatus.Failed
            or BillingCheckoutSessionStatus.Cancelled
            or BillingCheckoutSessionStatus.Expired)
            return;

        Status = BillingCheckoutSessionStatus.Expired;
        UpdatedAtUtc = NormalizeUtc(utcNow);
    }

    public void MarkCancelled(DateTime utcNow)
    {
        if (Status is BillingCheckoutSessionStatus.Completed
            or BillingCheckoutSessionStatus.Failed
            or BillingCheckoutSessionStatus.Cancelled
            or BillingCheckoutSessionStatus.Expired)
            return;

        Status = BillingCheckoutSessionStatus.Cancelled;
        UpdatedAtUtc = NormalizeUtc(utcNow);
    }

    private void EnsureMutableOpen()
    {
        if (Status is BillingCheckoutSessionStatus.Completed
            or BillingCheckoutSessionStatus.Failed
            or BillingCheckoutSessionStatus.Cancelled
            or BillingCheckoutSessionStatus.Expired)
        {
            throw new InvalidOperationException("Bu checkout session kapanmış; durum değiştirilemez.");
        }
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
}

