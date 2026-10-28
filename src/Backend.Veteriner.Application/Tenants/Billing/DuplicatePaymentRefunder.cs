using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Billing;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Tenants.Billing;

public sealed class DuplicatePaymentRefunder : IDuplicatePaymentRefunder
{
    /// <summary>Arka plan işi beş dakikada bir dener; üç başarısız denemeden sonra elle iade.</summary>
    public const int MaxAutomaticAttempts = 3;

    private readonly IBillingCheckoutProviderResolver _providers;
    private readonly IRepository<BillingCheckoutSession> _sessionsWrite;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DuplicatePaymentRefunder> _logger;

    public DuplicatePaymentRefunder(
        IBillingCheckoutProviderResolver providers,
        IRepository<BillingCheckoutSession> sessionsWrite,
        TimeProvider timeProvider,
        ILogger<DuplicatePaymentRefunder> logger)
    {
        _providers = providers;
        _sessionsWrite = sessionsWrite;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> RefundAsync(BillingCheckoutSession session, CancellationToken ct = default)
    {
        if (session.RefundStatus != BillingCheckoutRefundStatus.Pending)
            return false;

        var result = await _providers.Resolve(session.Provider).RefundPaymentAsync(session, ct);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (result.Succeeded)
        {
            session.RecordRefundSucceeded(utcNow);
        }
        else
        {
            var manual = !result.Retryable || session.RefundAttemptCount + 1 >= MaxAutomaticAttempts;
            session.RecordRefundFailed(utcNow, result.Reason ?? "", manual);
            _logger.LogWarning(
                "Çift ödeme iadesi başarısız. SessionId={SessionId} Attempt={Attempt} ManualRequired={Manual} Reason={Reason}",
                session.Id,
                session.RefundAttemptCount,
                manual,
                result.Reason);
        }

        await _sessionsWrite.UpdateAsync(session, ct);
        await _sessionsWrite.SaveChangesAsync(ct);
        return result.Succeeded;
    }
}
