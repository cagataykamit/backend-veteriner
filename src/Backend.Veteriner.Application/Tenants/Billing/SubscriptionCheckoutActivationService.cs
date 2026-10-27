using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.SalesPartners;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Tenants.Billing;

public sealed class SubscriptionCheckoutActivationService : ISubscriptionCheckoutActivationService
{
    private readonly IReadRepository<TenantSubscription> _subscriptionsRead;
    private readonly IRepository<TenantSubscription> _subscriptionsWrite;
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;
    private readonly IRepository<BillingCheckoutSession> _sessionsWrite;
    private readonly ISalesPartnerDiscountBilling _partnerDiscounts;
    private readonly TimeProvider _timeProvider;

    public SubscriptionCheckoutActivationService(
        IReadRepository<TenantSubscription> subscriptionsRead,
        IRepository<TenantSubscription> subscriptionsWrite,
        IReadRepository<BillingCheckoutSession> sessionsRead,
        IRepository<BillingCheckoutSession> sessionsWrite,
        ISalesPartnerDiscountBilling partnerDiscounts,
        TimeProvider timeProvider)
    {
        _subscriptionsRead = subscriptionsRead;
        _subscriptionsWrite = subscriptionsWrite;
        _sessionsRead = sessionsRead;
        _sessionsWrite = sessionsWrite;
        _partnerDiscounts = partnerDiscounts;
        _timeProvider = timeProvider;
    }

    public async Task<Result<SubscriptionCheckoutSessionDto>> TryActivateAsync(
        Guid checkoutSessionId,
        Guid? tenantIdConstraint,
        BillingProvider? providerMustMatch,
        string? externalReference,
        BillingActivationSource source,
        CancellationToken ct = default)
    {
        var session = await _sessionsRead.FirstOrDefaultAsync(new BillingCheckoutSessionByIdSpec(checkoutSessionId), ct);
        if (session is null)
            return Result<SubscriptionCheckoutSessionDto>.Failure("Subscriptions.CheckoutSessionNotFound", "Checkout session bulunamadı.");

        if (providerMustMatch.HasValue && session.Provider != providerMustMatch.Value)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Billing.ProviderMismatch",
                "Checkout session bu ödeme sağlayıcısı ile oluşturulmamış.");
        }

        if (tenantIdConstraint.HasValue && session.TenantId != tenantIdConstraint.Value)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Tenants.AccessDenied",
                "Checkout session bu kiracıya ait değil.");
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        if (session.Status == BillingCheckoutSessionStatus.Completed)
        {
            var subAfter = await _subscriptionsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(session.TenantId), ct);
            if (subAfter is null)
            {
                return Result<SubscriptionCheckoutSessionDto>.Failure(
                    "Subscriptions.NotFound",
                    "Bu kiracı için abonelik kaydı bulunamadı.");
            }

            if (subAfter.PlanCode == session.TargetPlanCode && subAfter.Status == TenantSubscriptionStatus.Active)
            {
                return Result<SubscriptionCheckoutSessionDto>.Success(MapDto(session, utcNow));
            }

            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.CheckoutActivationMismatch",
                "Checkout tamamlanmış ancak abonelik durumu beklenenle uyuşmuyor.");
        }

        // Panel finalize (Manual) ödemeyi sağlayıcıdan doğrulamaz; yalnız Manual (geliştirme/test) sağlayıcı oturumunu
        // etkinleştirebilir. Iyzico/Stripe oturumları imzası doğrulanan webhook ile etkinleşir (BILL-002).
        if (source == BillingActivationSource.Manual && session.Provider != BillingProvider.Manual)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Billing.ManualFinalizeNotAllowed",
                "Bu oturumun ödemesi sağlayıcı tarafından doğrulanmalıdır; elle tamamlanamaz.");
        }

        if (!session.IsOpen(utcNow))
        {
            if (session.Status is BillingCheckoutSessionStatus.Pending or BillingCheckoutSessionStatus.RedirectReady)
            {
                session.MarkExpired(utcNow);
                await _sessionsWrite.UpdateAsync(session, ct);
                await _sessionsWrite.SaveChangesAsync(ct);
            }

            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.CheckoutSessionNotOpen",
                "Checkout session kapanmış; finalize edilemez.");
        }

        var sub = await _subscriptionsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(session.TenantId), ct);
        if (sub is null)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        var completionReference = ResolveCompletionReference(session, externalReference);
        sub.ActivatePaidPlan(session.TargetPlanCode, session.BillingPeriod, utcNow);
        session.MarkCompleted(utcNow, completionReference);

        // Satış ortağı komisyonu yalnız gerçek tahsilatta (ADR-037 madde 4): imzası doğrulanan sağlayıcı olayı. Panel finalize
        // ödemeyi sağlayıcıdan doğrulamadığı için (Manual) komisyon üretmez. Aynı iş biriminde kaydedilir.
        if (source == BillingActivationSource.Webhook)
            await _partnerDiscounts.RecordCollectionAsync(session, utcNow, ct);

        await _subscriptionsWrite.UpdateAsync(sub, ct);
        await _sessionsWrite.UpdateAsync(session, ct);
        await _sessionsWrite.SaveChangesAsync(ct);

        return Result<SubscriptionCheckoutSessionDto>.Success(MapDto(session, utcNow));
    }

    private static SubscriptionCheckoutSessionDto MapDto(BillingCheckoutSession session, DateTime utcNow)
        => new(
            session.Id,
            session.TenantId,
            SubscriptionPlanCatalog.ToApiCode(session.CurrentPlanCode),
            SubscriptionPlanCatalog.ToApiCode(session.TargetPlanCode),
            session.Status,
            session.Provider,
            session.CheckoutUrl,
            session.IsOpen(utcNow),
            session.ExpiresAtUtc,
            null,
            null,
            null,
            TrialPlanChangeApplied: false);

    private static string? ResolveCompletionReference(BillingCheckoutSession session, string? incomingReference)
    {
        // Iyzico callback fallback zincirinde retrieve token'ı korunmalı.
        // PaymentId webhook/callback ile gelse bile token'ı ezmeyiz; token yoksa gelen referansı kullanırız.
        if (session.Provider == BillingProvider.Iyzico && !string.IsNullOrWhiteSpace(session.ExternalReference))
            return session.ExternalReference;

        return incomingReference;
    }
}
