using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Ai;

/// <summary>
/// AI isteğinden önce tek denetim yeri (ADR-044 madde 7–8): yapılandırma, klinikte açık olma, kliniğin kredi kotası ve fonu
/// (sigorta: aylık token/maliyet; <see cref="AiClinicQuota"/>), sistem aylık harcama, kullanıcı başına dakikalık istek. Sınırlar
/// istek öncesi denetlenir; aynı anda giden istekler sınırı en çok o isteklerin maliyeti kadar aşabilir (istek başına üst sınır
/// <see cref="AiOptions.MaxInputTokens"/> + <see cref="AiOptions.MaxOutputTokens"/>).
/// </summary>
public sealed class AiUsageGuard
{
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);

    private readonly AiOptions _options;
    private readonly IAiTextGenerator _generator;
    private readonly IReadRepository<ClinicAiSettings> _settings;
    private readonly IAiUsageReader _usage;
    private readonly AiCreditAccount _credits;
    private readonly TimeProvider _timeProvider;

    public AiUsageGuard(
        IOptions<AiOptions> options,
        IAiTextGenerator generator,
        IReadRepository<ClinicAiSettings> settings,
        IAiUsageReader usage,
        AiCreditAccount credits,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _generator = generator;
        _settings = settings;
        _usage = usage;
        _credits = credits;
        _timeProvider = timeProvider;
    }

    /// <summary>Genel anahtar, sağlayıcı anahtarı ve model fiyatı tamam mı (kliniğe bakmaz).</summary>
    public bool IsConfigured => _options.Enabled && _generator.IsConfigured && _options.PriceOf(_options.Model) is not null;

    /// <summary>
    /// Kliniğin AI kullanabilirliği (kullanıcı hız sınırı hariç): engel varsa ilk engel, yoksa <c>null</c>; kredi durumu
    /// (%80 uyarısı ve yenilenme tarihi için, ADR-044 madde 7).
    /// </summary>
    public async Task<AiAvailability> GetAvailabilityAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var isConfigured = IsConfigured;
        var settings = await _settings.FirstOrDefaultAsync(new ClinicAiSettingsByClinicSpec(tenantId, clinicId), ct);
        var isEnabled = ClinicAiSettings.IsEnabledOrDefault(settings);
        var quota = await _credits.GetQuotaAsync(tenantId, clinicId, ct);

        Error? blocker = null;
        if (!isConfigured)
            blocker = AiErrors.NotConfigured;
        else if (!isEnabled)
            blocker = AiErrors.ClinicNotEnabled;
        else if (quota.IsExhausted)
            blocker = AiErrors.ClinicQuotaExceeded;
        else if ((await _usage.SystemTotalsAsync(quota.MonthStartUtc, quota.ResetsAtUtc, ct)).CostUsd >= _options.SystemMonthlyCostLimitUsd)
            blocker = AiErrors.SystemBudgetExhausted;

        return new AiAvailability(isConfigured, isEnabled, blocker, quota);
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
}

/// <param name="Blocker">Kullanımı engelleyen ilk durum; yoksa <c>null</c>.</param>
/// <param name="Quota">Kliniğin bu ayki kredi durumu.</param>
public sealed record AiAvailability(bool IsConfigured, bool IsClinicEnabled, Error? Blocker, AiClinicQuota Quota)
{
    /// <summary>Kliniğin bu ayki kullanımının oranı (0–100).</summary>
    public int ClinicMonthUsedPercent => Quota.UsedPercent;
}

/// <summary>Denetimden geçen isteğin kullanacağı model, fiyat ve token sınırları.</summary>
public sealed record AiUsageGrant(string Model, AiModelPrice Price, int MaxInputTokens, int MaxOutputTokens);
