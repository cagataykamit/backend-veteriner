using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class StartSubscriptionCheckoutCommandHandler
    : IRequestHandler<StartSubscriptionCheckoutCommand, Result<SubscriptionCheckoutSessionDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptionsRead;
    private readonly IRepository<ScheduledSubscriptionPlanChange> _planChangesWrite;
    private readonly IReadRepository<ScheduledSubscriptionPlanChange> _planChangesRead;
    private readonly IRepository<BillingCheckoutSession> _checkoutSessionsWrite;
    private readonly IReadRepository<BillingCheckoutSession> _checkoutSessionsRead;
    private readonly IOptions<BillingOptions> _billingOptions;
    private readonly IBillingCheckoutProviderResolver _checkoutProviderResolver;
    private readonly ILogger<StartSubscriptionCheckoutCommandHandler> _logger;

    public StartSubscriptionCheckoutCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptionsRead,
        IRepository<ScheduledSubscriptionPlanChange> planChangesWrite,
        IReadRepository<ScheduledSubscriptionPlanChange> planChangesRead,
        IRepository<BillingCheckoutSession> checkoutSessionsWrite,
        IReadRepository<BillingCheckoutSession> checkoutSessionsRead,
        IOptions<BillingOptions> billingOptions,
        IBillingCheckoutProviderResolver checkoutProviderResolver,
        ILogger<StartSubscriptionCheckoutCommandHandler> logger)
    {
        _tenantContext = tenantContext;
        _tenants = tenants;
        _subscriptionsRead = subscriptionsRead;
        _planChangesWrite = planChangesWrite;
        _planChangesRead = planChangesRead;
        _checkoutSessionsWrite = checkoutSessionsWrite;
        _checkoutSessionsRead = checkoutSessionsRead;
        _billingOptions = billingOptions;
        _checkoutProviderResolver = checkoutProviderResolver;
        _logger = logger;
    }

    public async Task<Result<SubscriptionCheckoutSessionDto>> Handle(StartSubscriptionCheckoutCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } resolvedTenantId || resolvedTenantId != request.TenantId)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Tenants.AccessDenied",
                "Bu kiracı için checkout başlatma yetkisi yok.");
        }

        if (!SubscriptionPlanCatalog.TryParseApiCode(request.TargetPlanCode, out var targetPlanCode))
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.PlanCodeInvalid",
                "Hedef plan kodu geçersiz.");
        }

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(request.TenantId), ct);
        if (tenant is null)
            return Result<SubscriptionCheckoutSessionDto>.Failure("Tenants.NotFound", "Tenant bulunamadı.");
        if (!tenant.IsActive)
            return Result<SubscriptionCheckoutSessionDto>.Failure("Tenants.TenantInactive", "Pasif kiracı için checkout başlatılamaz.");

        var sub = await _subscriptionsRead.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(request.TenantId), ct);
        if (sub is null)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.NotFound",
                "Bu kiracı için abonelik kaydı bulunamadı.");
        }

        var now = DateTime.UtcNow;
        var effective = TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus(sub, now);
        if (effective == TenantSubscriptionStatus.Cancelled)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.TenantCancelled",
                "İptal edilmiş abonelik için checkout başlatılamaz.");
        }

        var decision = SubscriptionPlanChangeDecider.Decide(sub.PlanCode, targetPlanCode);
        if (decision == SubscriptionPlanChangeDecision.Same)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.SamePlanAlreadyActive",
                "Kiracı zaten seçilen planda.");
        }

        if (decision == SubscriptionPlanChangeDecision.Downgrade)
        {
            return Result<SubscriptionCheckoutSessionDto>.Failure(
                "Subscriptions.DowngradeMustBeScheduled",
                "Downgrade için checkout açılmaz; plan değişikliği schedule edilmelidir.");
        }

        var openPlanChange = await _planChangesRead.FirstOrDefaultAsync(new OpenScheduledPlanChangeByTenantSpec(request.TenantId), ct);
        if (openPlanChange is not null)
        {
            openPlanChange.Cancel(DateTime.UtcNow);
            await _planChangesWrite.UpdateAsync(openPlanChange, ct);
            await _planChangesWrite.SaveChangesAsync(ct);
        }

        string? chargeCurrency = null;
        long? proratedChargeMinor = null;
        decimal? prorationRatio = null;
        if (decision == SubscriptionPlanChangeDecision.Upgrade)
        {
            var prices = _billingOptions.Value;
            if (!TryGetPlanPriceMinor(prices, sub.PlanCode, out var currentPriceMinor)
                || !TryGetPlanPriceMinor(prices, targetPlanCode, out var targetPriceMinor))
            {
                return Result<SubscriptionCheckoutSessionDto>.Failure(
                    "Billing.PlanPriceNotConfigured",
                    "Proration için plan fiyatları (minor unit) yapılandırılmalı.");
            }

            var period = TenantSubscriptionPeriodCalculator.ResolveCurrentWindow(sub, now);
            var proration = SubscriptionProrationCalculator.Calculate(
                now,
                period.PeriodStartUtc,
                period.PeriodEndUtc,
                currentPriceMinor,
                targetPriceMinor);

            if (proration.PriceDiffMinor <= 0)
            {
                return Result<SubscriptionCheckoutSessionDto>.Failure(
                    "Subscriptions.UpgradePriceDiffInvalid",
                    "Upgrade fiyat farkı hesaplanamadı.");
            }

            chargeCurrency = ResolvePlanPriceCurrency(prices);
            proratedChargeMinor = Math.Max(1, proration.ProratedChargeMinor);
            prorationRatio = proration.ProrationRatio;
        }

        var open = await _checkoutSessionsRead.FirstOrDefaultAsync(new OpenBillingCheckoutSessionByTenantSpec(request.TenantId, now), ct);
        if (open is not null)
        {
            if (open.TargetPlanCode == targetPlanCode)
                return Result<SubscriptionCheckoutSessionDto>.Success(Map(open, now, chargeCurrency, proratedChargeMinor, prorationRatio));

            open.MarkCancelled(now);
            await _checkoutSessionsWrite.UpdateAsync(open, ct);
            await _checkoutSessionsWrite.SaveChangesAsync(ct);
        }

        var billingOpts = _billingOptions.Value;
        var resolved = BillingCheckoutProviderResolution.Resolve(billingOpts);
        if (!resolved.IsSuccess)
        {
            _logger.LogWarning(
                "Subscription checkout başlatılamadı: {Code} — {Message}",
                resolved.Error.Code,
                resolved.Error.Message);
            return Result<SubscriptionCheckoutSessionDto>.Failure(resolved.Error.Code, resolved.Error.Message);
        }

        var providerKind = resolved.Value!;
        _logger.LogInformation(
            "Subscription checkout başlatılıyor: Tenant {TenantId}, hedef plan {TargetPlan}, billing provider {Provider}.",
            request.TenantId,
            request.TargetPlanCode,
            providerKind);

        var expiresAt = now.AddMinutes(SubscriptionCheckoutDefaults.SessionTtlMinutes);
        var session = BillingCheckoutSession.CreatePending(
            request.TenantId,
            sub.PlanCode,
            targetPlanCode,
            providerKind,
            now,
            expiresAt);

        await _checkoutSessionsWrite.AddAsync(session, ct);
        await _checkoutSessionsWrite.SaveChangesAsync(ct);

        var billingProvider = _checkoutProviderResolver.Resolve(providerKind);
        var prepare = await billingProvider.PrepareCheckoutAsync(session, chargeCurrency, proratedChargeMinor, prorationRatio, ct);
        if (!prepare.IsSuccess)
        {
            await _checkoutSessionsWrite.DeleteAsync(session, ct);
            await _checkoutSessionsWrite.SaveChangesAsync(ct);
            return Result<SubscriptionCheckoutSessionDto>.Failure(prepare.Error.Code, prepare.Error.Message);
        }

        var prepared = prepare.Value!;
        chargeCurrency = prepared.ChargeCurrencyCode ?? chargeCurrency;
        proratedChargeMinor = prepared.ChargeAmountMinor ?? proratedChargeMinor;
        prorationRatio = prepared.ProrationRatio ?? prorationRatio;
        var afterPrepare = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(prepared.CheckoutUrl))
        {
            session.SetRedirectReady(prepared.CheckoutUrl, prepared.ExternalReference, afterPrepare);
            await _checkoutSessionsWrite.UpdateAsync(session, ct);
            await _checkoutSessionsWrite.SaveChangesAsync(ct);
        }

        return Result<SubscriptionCheckoutSessionDto>.Success(Map(session, afterPrepare, chargeCurrency, proratedChargeMinor, prorationRatio));
    }

    private static SubscriptionCheckoutSessionDto Map(
        BillingCheckoutSession session,
        DateTime utcNow,
        string? chargeCurrency = null,
        long? proratedChargeMinor = null,
        decimal? prorationRatio = null)
    {
        return new SubscriptionCheckoutSessionDto(
            session.Id,
            session.TenantId,
            SubscriptionPlanCatalog.ToApiCode(session.CurrentPlanCode),
            SubscriptionPlanCatalog.ToApiCode(session.TargetPlanCode),
            session.Status,
            session.Provider,
            session.CheckoutUrl,
            session.IsOpen(utcNow),
            session.ExpiresAtUtc,
            chargeCurrency,
            proratedChargeMinor,
            prorationRatio);
    }

    private static string ResolvePlanPriceCurrency(BillingOptions options)
    {
        var configured = options.PlanPriceCurrency?.Trim();
        return string.IsNullOrWhiteSpace(configured) ? "TRY" : configured.ToUpperInvariant();
    }

    private static bool TryGetPlanPriceMinor(BillingOptions options, SubscriptionPlanCode code, out long minor)
    {
        var apiCode = SubscriptionPlanCatalog.ToApiCode(code);
        if (TryGetDictionaryValue(options.PlanPricesMinor, apiCode, out minor))
            return true;

        if (options.Iyzico.PlanPricesTry is { Count: > 0 }
            && TryGetDictionaryValue(options.Iyzico.PlanPricesTry, apiCode, out var amountTry))
        {
            minor = (long)Math.Round(amountTry * 100m, MidpointRounding.AwayFromZero);
            return minor > 0;
        }

        minor = 0;
        return false;
    }

    private static bool TryGetDictionaryValue<T>(IReadOnlyDictionary<string, T>? map, string key, out T value)
    {
        value = default!;
        if (map is null || map.Count == 0)
            return false;

        if (map.TryGetValue(key, out value!))
            return true;

        foreach (var pair in map)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        return false;
    }
}

