using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// AI isteğinden önce tek denetim yeri (ADR-044 madde 7–8): yapılandırma, klinikte açık olma, klinik aylık token/maliyet,
/// sistem aylık harcama, kullanıcı başına dakikalık istek. Sınırlar istek öncesi denetlenir; aynı anda giden istekler sınırı
/// en çok o isteklerin maliyeti kadar aşabilir (istek başına üst sınır <see cref="AiOptions.MaxInputTokens"/> +
/// <see cref="AiOptions.MaxOutputTokens"/>).
/// </summary>
public sealed class AiUsageGuard
{
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);

    private readonly AiOptions _options;
    private readonly IAiTextGenerator _generator;
    private readonly IReadRepository<ClinicAiSettings> _settings;
    private readonly IAiUsageReader _usage;
    private readonly TimeProvider _timeProvider;

    public AiUsageGuard(
        IOptions<AiOptions> options,
        IAiTextGenerator generator,
        IReadRepository<ClinicAiSettings> settings,
        IAiUsageReader usage,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _generator = generator;
        _settings = settings;
        _usage = usage;
        _timeProvider = timeProvider;
    }

    /// <summary>Genel anahtar, sağlayıcı anahtarı ve model fiyatı tamam mı (kliniğe bakmaz).</summary>
    public bool IsConfigured => _options.Enabled && _generator.IsConfigured && _options.PriceOf(_options.Model) is not null;

    /// <summary>
    /// Kliniğin AI kullanabilirliği (kullanıcı hız sınırı hariç): engel varsa ilk engel, yoksa <c>null</c>. Kullanım oranı
    /// token ve maliyet oranlarından büyüğüdür (%; %80 uyarısı için, ADR-044 madde 7).
    /// </summary>
    public async Task<AiAvailability> GetAvailabilityAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var isConfigured = IsConfigured;
        var settings = await _settings.FirstOrDefaultAsync(new ClinicAiSettingsByClinicSpec(tenantId, clinicId), ct);
        var isEnabled = ClinicAiSettings.IsEnabledOrDefault(settings);

        var (monthStart, monthEnd) = MonthBounds();
        var clinic = await _usage.ClinicTotalsAsync(clinicId, monthStart, monthEnd, ct);
        var usedPercent = UsedPercent(clinic);

        Error? blocker = null;
        if (!isConfigured)
            blocker = AiErrors.NotConfigured;
        else if (!isEnabled)
            blocker = AiErrors.ClinicNotEnabled;
        else if (usedPercent >= 100)
            blocker = AiErrors.ClinicQuotaExceeded;
        else if ((await _usage.SystemTotalsAsync(monthStart, monthEnd, ct)).CostUsd >= _options.SystemMonthlyCostLimitUsd)
            blocker = AiErrors.SystemBudgetExhausted;

        return new AiAvailability(isConfigured, isEnabled, blocker, usedPercent);
    }

    public async Task<Result<AiUsageGrant>> CheckAsync(Guid tenantId, Guid clinicId, Guid userId, CancellationToken ct)
    {
        var availability = await GetAvailabilityAsync(tenantId, clinicId, ct);
        if (availability.Blocker is { } blocker)
            return Result<AiUsageGrant>.Failure(blocker);

        var since = _timeProvider.GetUtcNow().UtcDateTime - RateWindow;
        if (await _usage.CountUserRequestsSinceAsync(userId, since, ct) >= _options.UserRequestsPerMinute)
            return Result<AiUsageGrant>.Failure(AiErrors.RateLimited);

        return Result<AiUsageGrant>.Success(new AiUsageGrant(
            _options.Model, _options.PriceOf(_options.Model)!, _options.MaxInputTokens, _options.MaxOutputTokens));
    }

    private (DateTime Start, DateTime End) MonthBounds()
        => OperationPeriodBounds.MonthForUtcNow(_timeProvider.GetUtcNow().UtcDateTime);

    private int UsedPercent(AiUsageTotals totals)
    {
        var tokenRatio = _options.ClinicMonthlyTokenLimit > 0 ? (decimal)totals.Tokens / _options.ClinicMonthlyTokenLimit : 1m;
        var costRatio = _options.ClinicMonthlyCostLimitUsd > 0 ? totals.CostUsd / _options.ClinicMonthlyCostLimitUsd : 1m;
        return (int)Math.Min(100m, Math.Floor(Math.Max(tokenRatio, costRatio) * 100m));
    }
}

/// <param name="Blocker">Kullanımı engelleyen ilk durum; yoksa <c>null</c>.</param>
/// <param name="ClinicMonthUsedPercent">Kliniğin bu ayki kullanımının sınıra oranı (0–100).</param>
public sealed record AiAvailability(bool IsConfigured, bool IsClinicEnabled, Error? Blocker, int ClinicMonthUsedPercent);

/// <summary>Denetimden geçen isteğin kullanacağı model, fiyat ve token sınırları.</summary>
public sealed record AiUsageGrant(string Model, AiModelPrice Price, int MaxInputTokens, int MaxOutputTokens);
