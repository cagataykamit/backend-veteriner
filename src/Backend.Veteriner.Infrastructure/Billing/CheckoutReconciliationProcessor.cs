using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Billing;

/// <summary>
/// Kullanıcı ödeme sonrası dönmediyse ve webhook da ulaşmadıysa ödeme boşa gitmesin: son oturumlar sağlayıcıya sorulur,
/// ödenmiş olan etkinleşir (<see cref="ICheckoutPaymentReconciler"/>). Ardından bekleyen çift ödemeler iade edilir
/// (<see cref="IDuplicatePaymentRefunder"/>, BILL-005). Tek tur; periyodik çağrı barındırılan serviste.
/// </summary>
public sealed class CheckoutReconciliationProcessor
{
    private readonly IReadRepository<BillingCheckoutSession> _sessionsRead;
    private readonly ICheckoutPaymentReconciler _reconciler;
    private readonly IDuplicatePaymentRefunder _refunder;
    private readonly ITenantScope _tenantScope;
    private readonly CheckoutReconciliationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CheckoutReconciliationProcessor> _logger;

    public CheckoutReconciliationProcessor(
        IReadRepository<BillingCheckoutSession> sessionsRead,
        ICheckoutPaymentReconciler reconciler,
        IDuplicatePaymentRefunder refunder,
        ITenantScope tenantScope,
        IOptions<CheckoutReconciliationOptions> options,
        TimeProvider timeProvider,
        ILogger<CheckoutReconciliationProcessor> logger)
    {
        _sessionsRead = sessionsRead;
        _reconciler = reconciler;
        _refunder = refunder;
        _tenantScope = tenantScope;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <returns>Sağlayıcıda ödenmiş bulunan oturum sayısı.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var since = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-Math.Max(1, _options.LookbackMinutes));

        // Oturumları bulmak kiracılar arasıdır (ADR-040); her oturum kendi kiracısının kapsamında uzlaştırılır.
        IReadOnlyList<BillingCheckoutSession> sessions;
        using (_tenantScope.AsSystem())
            sessions = await _sessionsRead.ListAsync(new UnsettledProviderCheckoutSessionsSpec(since, Math.Max(1, _options.BatchSize)), ct);

        var paid = 0;
        foreach (var session in sessions)
        {
            try
            {
                using var tenant = _tenantScope.UseTenant(session.TenantId);
                if (await _reconciler.ReconcileAsync(session, tenantIdConstraint: null, ct) == CheckoutPageState.Paid)
                    paid++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Ödeme oturumu uzlaştırılamadı. SessionId={SessionId}", session.Id);
            }
        }

        if (paid > 0)
            _logger.LogInformation("Ödeme uzlaştırması: {Paid} oturum sağlayıcıda ödenmiş bulundu.", paid);

        return paid;
    }

    /// <returns>Sağlayıcıda iadesi yapılan çift ödeme sayısı.</returns>
    public async Task<int> RefundPendingOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<BillingCheckoutSession> sessions;
        using (_tenantScope.AsSystem())
            sessions = await _sessionsRead.ListAsync(new PendingRefundCheckoutSessionsSpec(Math.Max(1, _options.BatchSize)), ct);

        var refunded = 0;
        foreach (var session in sessions)
        {
            try
            {
                using var tenant = _tenantScope.UseTenant(session.TenantId);
                if (await _refunder.RefundAsync(session, ct))
                    refunded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Çift ödeme iadesi işlenemedi. SessionId={SessionId}", session.Id);
            }
        }

        if (refunded > 0)
            _logger.LogInformation("Çift ödeme iadesi: {Refunded} ödeme sağlayıcıda geri verildi.", refunded);

        return refunded;
    }
}
