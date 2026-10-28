using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.SalesPartners;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Tenants.Billing;

public sealed class SubscriptionCheckoutActivationService : ISubscriptionCheckoutActivationService
{
    private readonly IReadRepository<TenantSubscription> _subscriptionsRead;
    private readonly IRepository<TenantSubscription> _subscriptionsWrite;
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;
    private readonly IRepository<BillingCheckoutSession> _sessionsWrite;
    private readonly ITenantScope _tenantScope;
    private readonly ISalesPartnerDiscountBilling _partnerDiscounts;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SubscriptionCheckoutActivationService> _logger;

    public SubscriptionCheckoutActivationService(
        IReadRepository<TenantSubscription> subscriptionsRead,
        IRepository<TenantSubscription> subscriptionsWrite,
        IReadRepository<BillingCheckoutSession> sessionsRead,
        IRepository<BillingCheckoutSession> sessionsWrite,
        ITenantScope tenantScope,
        ISalesPartnerDiscountBilling partnerDiscounts,
        TimeProvider timeProvider,
        ILogger<SubscriptionCheckoutActivationService> logger)
    {
        _subscriptionsRead = subscriptionsRead;
        _subscriptionsWrite = subscriptionsWrite;
        _sessionsRead = sessionsRead;
        _sessionsWrite = sessionsWrite;
        _tenantScope = tenantScope;
        _partnerDiscounts = partnerDiscounts;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<SubscriptionCheckoutSessionDto>> TryActivateAsync(
        Guid checkoutSessionId,
        Guid? tenantIdConstraint,
        BillingProvider? providerMustMatch,
        string? externalReference,
        BillingActivationSource source,
        CancellationToken ct = default)
    {
        // Webhook'ta kiracı yoktur (tenantIdConstraint null): oturum kimliğiyle kiracılar arası aranır ve kiracı
        // oturumdan çözülür (ADR-040). Panel ucunda istek kiracısı ve kısıt geçerlidir.
        BillingCheckoutSession? session;
        using (tenantIdConstraint is null ? _tenantScope.AsSystem() : null)
            session = await _sessionsRead.FirstOrDefaultAsync(new BillingCheckoutSessionByIdSpec(checkoutSessionId), ct);
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

        using var tenantScope = _tenantScope.UseTenant(session.TenantId);
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

        if (session.RefundStatus != BillingCheckoutRefundStatus.None)
            return DuplicatePayment();

        // Panel finalize (Manual) ödemeyi sağlayıcıdan doğrulamaz; yalnız Manual (geliştirme/test) sağlayıcı oturumunu
        // etkinleştirebilir. Iyzico/Stripe oturumları imzası doğrulanan webhook ile etkinleşir (BILL-002).
        if (source == BillingActivationSource.Manual && session.Provider != BillingProvider.Manual)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Billing.ManualFinalizeNotAllowed",
                "Bu oturumun ödemesi sağlayıcı tarafından doğrulanmalıdır; elle tamamlanamaz.");
        }

        // Sağlayıcının doğruladığı ödeme (webhook ya da sorgu) oturum kapanmış olsa da (süresi dolmuş, iptal, başarısız deneme)
        // etkinleşir: para alınmıştır. Doğrulanmamış elle tamamlama yalnız açık oturumda olur.
        var verifiedPayment = source is BillingActivationSource.Webhook or BillingActivationSource.ProviderVerified;
        var wasOpen = session.IsOpen(utcNow);
        if (!wasOpen && !verifiedPayment)
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

        // Çift ödeme (BILL-005): bu oturum açıldıktan sonra başka bir oturum paketi açtıysa iki ödeme aynı denemeye aittir
        // (ör. paket değiştirilip eski ödeme sayfası da ödendi). İlk işlenen geçerlidir; bu ödeme iade edilir, paket değişmez.
        // Oturum açılmadan önce tamamlanmış ödeme ayrı bir satın almadır (ör. aylıktan sonra yıllığa geçiş).
        if (verifiedPayment)
        {
            var settledBefore = await _sessionsRead.FirstOrDefaultAsync(
                new CheckoutSessionCompletedAfterSpec(session.TenantId, session.Id, session.CreatedAtUtc), ct);
            if (settledBefore is not null)
            {
                _logger.LogWarning(
                    "Çift ödeme: oturum paket açmadan iadeye alındı. SessionId={SessionId} SettledSessionId={SettledSessionId} Source={Source}",
                    session.Id,
                    settledBefore.Id,
                    source);
                session.MarkDuplicatePayment(utcNow);
                await _sessionsWrite.UpdateAsync(session, ct);
                await _sessionsWrite.SaveChangesAsync(ct);
                return DuplicatePayment();
            }
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
        if (wasOpen)
        {
            session.MarkCompleted(utcNow, completionReference);
        }
        else
        {
            // Olağan dışı: kullanıcı dönmedi, webhook geç geldi ya da oturum ödeme sırasında yenilendi. İzlenebilsin diye kayıt.
            _logger.LogWarning(
                "Kapanmış checkout oturumu doğrulanmış ödemeyle tamamlandı. SessionId={SessionId} PreviousStatus={Status} Source={Source}",
                session.Id,
                session.Status,
                source);
            session.MarkCompletedByVerifiedPayment(utcNow, completionReference);
        }

        // Satış ortağı komisyonu yalnız gerçek tahsilatta (ADR-037 madde 4): imzası doğrulanan sağlayıcı olayı ya da backend'in
        // sağlayıcıdan doğruladığı ödeme. Panel finalize ödemeyi doğrulamadığı için (Manual) komisyon üretmez. Aynı iş biriminde
        // kaydedilir; ikinci yol oturumu tamamlanmış bulduğundan komisyon iki kez yazılmaz.
        if (source is BillingActivationSource.Webhook or BillingActivationSource.ProviderVerified)
            await _partnerDiscounts.RecordCollectionAsync(session, utcNow, ct);

        // Paket açıldı: kiracının başka açık oturumu kapanır. "Ödemeye devam et" o sayfaya dönmez; sağlayıcıda hâlâ ödenirse
        // yukarıdaki kural onu çift ödeme sayar. Bundan sonra açılan oturum yeni bir satın almadır.
        var otherOpen = await _sessionsRead.ListAsync(new OpenBillingCheckoutSessionByTenantSpec(session.TenantId, utcNow), ct);
        foreach (var open in otherOpen.Where(x => x.Id != session.Id))
        {
            open.MarkCancelled(utcNow);
            await _sessionsWrite.UpdateAsync(open, ct);
        }

        await _subscriptionsWrite.UpdateAsync(sub, ct);
        await _sessionsWrite.UpdateAsync(session, ct);
        await _sessionsWrite.SaveChangesAsync(ct);

        return Result<SubscriptionCheckoutSessionDto>.Success(MapDto(session, utcNow));
    }

    private static Result<SubscriptionCheckoutSessionDto> DuplicatePayment()
        => Result<SubscriptionCheckoutSessionDto>.Failure(
            ISubscriptionCheckoutActivationService.DuplicatePaymentErrorCode,
            "Bu ödeme aynı ödeme denemesinde ikinci ödeme; paket değişmedi, ödeme iade edilecek.");

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
