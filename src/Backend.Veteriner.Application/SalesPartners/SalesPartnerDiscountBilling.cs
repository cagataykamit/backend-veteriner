using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.SalesPartners;

/// <summary>Abonelik ödeme akışının satış ortağı kodu kancaları (ADR-037): checkout'ta indirim, tahsilat olayında komisyon.</summary>
public interface ISalesPartnerDiscountBilling
{
    /// <summary>
    /// Checkout tutarına kiracının kod indirimini uygular. Tutar zaten dönem tutarıdır (yenileme fiyatı, yükseltme farkı ya da
    /// dönem değişimi; yıllık indirim dahil), bu yüzden yalnız kod indirimi eklenir; uygunluk dönem türüne göredir (ADR-037).
    /// </summary>
    Task<SubscriptionPriceBreakdown> PriceAsync(Guid tenantId, long amountMinor, BillingPeriodKind period, DateTime pricedAtUtc, CancellationToken ct);

    /// <summary>
    /// Ödeme sağlayıcısının doğruladığı tahsilatı kiracının kod kullanımına yazar (kaydetmez; çağıran aynı iş biriminde kaydeder).
    /// Kodu olmayan kiracıda, fiyat kaydı olmayan oturumda ya da pencere dışında hiçbir şey yazmaz.
    /// </summary>
    Task RecordCollectionAsync(BillingCheckoutSession session, DateTime collectedAtUtc, CancellationToken ct);
}

public sealed class SalesPartnerDiscountBilling : ISalesPartnerDiscountBilling
{
    private readonly IRepository<DiscountRedemption> _redemptions;
    private readonly ILogger<SalesPartnerDiscountBilling> _logger;

    public SalesPartnerDiscountBilling(IRepository<DiscountRedemption> redemptions, ILogger<SalesPartnerDiscountBilling> logger)
    {
        _redemptions = redemptions;
        _logger = logger;
    }

    public async Task<SubscriptionPriceBreakdown> PriceAsync(Guid tenantId, long amountMinor, BillingPeriodKind period, DateTime pricedAtUtc, CancellationToken ct)
    {
        var redemption = await _redemptions.FirstOrDefaultAsync(new DiscountRedemptionByTenantSpec(tenantId), ct);
        return SubscriptionPricing.ForAmount(amountMinor, period, redemption, pricedAtUtc);
    }

    public async Task RecordCollectionAsync(BillingCheckoutSession session, DateTime collectedAtUtc, CancellationToken ct)
    {
        if (session.GrossAmountMinor is not { } gross || gross <= 0)
            return;

        var redemption = await _redemptions.FirstOrDefaultAsync(new DiscountRedemptionByTenantSpec(session.TenantId), ct);
        if (redemption is null)
            return;

        var collection = redemption.RecordCollection(
            session.Id,
            session.BillingPeriod,
            gross,
            session.DiscountAmountMinor ?? 0,
            session.CreatedAtUtc,
            collectedAtUtc);

        if (collection is not null)
        {
            _logger.LogInformation(
                "Satış ortağı tahsilatı yazıldı. TenantId={TenantId} CheckoutSessionId={SessionId} NetMinor={NetMinor} CommissionMinor={CommissionMinor}",
                session.TenantId,
                session.Id,
                collection.NetAmountMinor,
                collection.CommissionAmountMinor);
        }
    }
}
