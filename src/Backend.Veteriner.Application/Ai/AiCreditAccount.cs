using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// Kliniğin AI kredi hesabı (AI-007, ADR-044): bu ayın kotası (paket hediyesi, <see cref="AiOptions.PlanMonthlyCredits"/>),
/// fon bakiyesi, harcama ve %80 / bitiş uyarısı. Kredi gerçek token'ın tahmini maliyetinden hesaplanır
/// (<see cref="AiOptions.CreditsOf"/>). Bakiye kullanım ve fon kayıtlarının toplamıdır; değişen sayaç tutulmaz.
/// </summary>
public sealed class AiCreditAccount
{
    private readonly AiOptions _options;
    private readonly IAiUsageReader _usage;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IRepository<ClinicAiSettings> _settings;
    private readonly IUserNotificationPublisher _notifications;
    private readonly TimeProvider _timeProvider;

    public AiCreditAccount(
        IOptions<AiOptions> options,
        IAiUsageReader usage,
        IReadRepository<TenantSubscription> subscriptions,
        IRepository<ClinicAiSettings> settings,
        IUserNotificationPublisher notifications,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _usage = usage;
        _subscriptions = subscriptions;
        _settings = settings;
        _notifications = notifications;
        _timeProvider = timeProvider;
    }

    /// <summary>Bu ayın (İstanbul takvim ayı) başı ve sonu.</summary>
    public (DateTime Start, DateTime End) CurrentMonth()
        => OperationPeriodBounds.MonthForUtcNow(_timeProvider.GetUtcNow().UtcDateTime);

    public async Task<AiClinicQuota> GetQuotaAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var (start, end) = CurrentMonth();
        var totals = await _usage.ClinicCreditTotalsAsync(clinicId, start, end, ct);
        var subscription = await _subscriptions.FirstOrDefaultAsync(new TenantSubscriptionByTenantIdSpec(tenantId), ct);
        return Build(subscription, totals, start, end);
    }

    /// <summary>Toplamlardan kota durumu (platform yönetimi birden çok kliniği tek seferde okur).</summary>
    public AiClinicQuota Build(TenantSubscription? subscription, AiClinicCreditTotals totals, DateTime monthStartUtc, DateTime monthEndUtc)
        => new(
            MonthlyCreditsOf(subscription),
            totals.MonthCredits - totals.MonthFundCredits,
            totals.MonthFundCredits,
            totals.FundGranted - totals.FundUsed,
            totals.MonthTokens,
            totals.MonthCostUsd,
            _options.ClinicMonthlyTokenLimit,
            _options.ClinicMonthlyCostLimitUsd,
            monthStartUtc,
            monthEndUtc);

    /// <summary>
    /// Paket hediyesi ücretli abonelikle başlar: deneme süresindeki (<see cref="TenantSubscriptionStatus.Trialing"/>) kiracıya
    /// aylık kota verilmez, yalnız fon kullanılabilir (ADR-044 madde 5, 2026-11-20 kullanıcı kararı).
    /// </summary>
    private int MonthlyCreditsOf(TenantSubscription? subscription)
        => subscription is { Status: not TenantSubscriptionStatus.Trialing }
            ? _options.MonthlyCreditsFor(SubscriptionPlanCatalog.ToApiCode(subscription.PlanCode))
            : 0;

    /// <summary>
    /// Tamamlanan isteğin kredisini hesaplar ve kota/fon olarak böler; eşik aşıldıysa klinik yöneticilerine ayda bir kez uyarı
    /// ekler. Kayıtlar izlenir; çağıran kullanım kaydıyla birlikte kaydeder.
    /// </summary>
    public async Task<AiCreditCharge> ChargeAsync(Guid tenantId, Guid clinicId, long tokens, decimal costUsd, CancellationToken ct)
    {
        var credits = _options.CreditsOf(costUsd);
        if (credits <= 0)
            return AiCreditCharge.None;

        var before = await GetQuotaAsync(tenantId, clinicId, ct);
        var (fromQuota, fromFund) = before.Split(credits);
        var after = before.After(fromQuota, fromFund, tokens, costUsd);

        if (after.WarningLevel > 0)
        {
            var settings = await _settings.FirstOrDefaultAsync(new ClinicAiSettingsByClinicSpec(tenantId, clinicId), ct);
            if (settings is not null && settings.TryMarkQuotaWarning(after.MonthStartUtc, after.WarningLevel))
                await _notifications.AiQuotaWarningAsync(tenantId, clinicId, after.IsExhausted, after.ResetsAtUtc, ct);
        }

        return new AiCreditCharge(credits, fromFund);
    }
}
