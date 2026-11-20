using System.Globalization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Ai;

// Platform yönetimi (ADR-044; AI-007 kota, AI-008 maliyet). Yalnız platform yöneticisi, kiracılar arası
// (PlatformTenantScope). Sözleşme: docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §3. Klinik metin (istem, yanıt) hiçbir yanıtta yoktur.

/// <summary>Platform yönetimi: kliniğin AI ayarı ve bu ayın kredi durumu.</summary>
/// <param name="PlanCode">Kiracının paketi (API kodu); abonelik yoksa <c>null</c>.</param>
/// <param name="MonthlyCredits">Bu ayın kotası (paket hediyesi).</param>
/// <param name="FundCredits">Fon bakiyesi (devreder).</param>
/// <param name="UsedPercent">Bu ayın kullanım oranı (0–100; kliniğin gördüğüyle aynı).</param>
public sealed record AiAdminClinicDto(
    Guid TenantId,
    string TenantName,
    Guid ClinicId,
    string ClinicName,
    bool IsClinicActive,
    string? PlanCode,
    bool IsEnabled,
    DateTime? UpdatedAtUtc,
    int MonthlyCredits,
    decimal CreditsUsedThisMonth,
    decimal CreditsRemaining,
    decimal FundCredits,
    int UsedPercent,
    DateTime QuotaResetsAtUtc);

/// <summary>Platform yönetimi ekranları için klinik satırı kurar (ayar, paket, kredi durumu tek seferde).</summary>
public sealed class AiAdminClinicReader
{
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<TenantSubscription> _subscriptions;
    private readonly IReadRepository<ClinicAiSettings> _settings;
    private readonly IAiUsageReader _usage;
    private readonly AiCreditAccount _credits;

    public AiAdminClinicReader(
        IReadRepository<Tenant> tenants,
        IReadRepository<TenantSubscription> subscriptions,
        IReadRepository<ClinicAiSettings> settings,
        IAiUsageReader usage,
        AiCreditAccount credits)
    {
        _tenants = tenants;
        _subscriptions = subscriptions;
        _settings = settings;
        _usage = usage;
        _credits = credits;
    }

    public async Task<IReadOnlyList<AiAdminClinicDto>> ReadAsync(IReadOnlyCollection<Clinic> clinics, CancellationToken ct)
    {
        if (clinics.Count == 0)
            return [];

        var clinicIds = clinics.Select(c => c.Id).ToArray();
        var tenantIds = clinics.Select(c => c.TenantId).Distinct().ToArray();
        var tenantNames = (await _tenants.ListAsync(new TenantsForAiAdminSpec(tenantIds), ct)).ToDictionary(t => t.Id, t => t.Name);
        var subscriptions = (await _subscriptions.ListAsync(new TenantSubscriptionsForAiAdminSpec(tenantIds), ct))
            .GroupBy(s => s.TenantId)
            .ToDictionary(g => g.Key, g => g.First());
        var settings = (await _settings.ListAsync(new ClinicAiSettingsByClinicIdsSpec(clinicIds), ct)).ToDictionary(s => s.ClinicId);
        var (monthStart, monthEnd) = _credits.CurrentMonth();
        var totals = await _usage.ClinicsCreditTotalsAsync(clinicIds, monthStart, monthEnd, ct);

        return clinics
            .Select(c =>
            {
                var subscription = subscriptions.GetValueOrDefault(c.TenantId);
                SubscriptionPlanCode? plan = subscription?.PlanCode;
                var quota = _credits.Build(subscription, totals.GetValueOrDefault(c.Id, AiClinicCreditTotals.Zero), monthStart, monthEnd);
                var row = settings.GetValueOrDefault(c.Id);
                return new AiAdminClinicDto(
                    c.TenantId,
                    tenantNames.GetValueOrDefault(c.TenantId, string.Empty),
                    c.Id,
                    c.Name,
                    c.IsActive,
                    plan is { } p ? SubscriptionPlanCatalog.ToApiCode(p) : null,
                    ClinicAiSettings.IsEnabledOrDefault(row),
                    row?.UpdatedAtUtc ?? row?.CreatedAtUtc,
                    quota.MonthlyCredits,
                    decimal.Round(quota.MonthCreditsUsed, 2),
                    decimal.Round(quota.CreditsRemaining, 2),
                    decimal.Round(quota.FundRemaining, 2),
                    quota.UsedPercent,
                    quota.ResetsAtUtc);
            })
            .ToList();
    }

    public async Task<AiAdminClinicDto> ReadAsync(Clinic clinic, CancellationToken ct) => (await ReadAsync([clinic], ct))[0];
}

/// <summary>
/// Platform yönetimi: klinik listesi. Arama yoksa AI ayarı olan (bir kez açılmış) klinikler, açık olanlar önce; arama varsa klinik
/// ya da kiracı adına uyan en çok 50 klinik (açılacak kliniği bulmak için).
/// </summary>
public sealed record GetAiAdminClinicsQuery(string? Search) : IRequest<Result<IReadOnlyList<AiAdminClinicDto>>>;

public sealed class GetAiAdminClinicsQueryHandler : IRequestHandler<GetAiAdminClinicsQuery, Result<IReadOnlyList<AiAdminClinicDto>>>
{
    public const int SearchMinLength = 2;
    public const int MaxResults = 50;

    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<ClinicAiSettings> _settings;
    private readonly AiAdminClinicReader _reader;

    public GetAiAdminClinicsQueryHandler(
        IReadRepository<Clinic> clinics,
        IReadRepository<Tenant> tenants,
        IReadRepository<ClinicAiSettings> settings,
        AiAdminClinicReader reader)
    {
        _clinics = clinics;
        _tenants = tenants;
        _settings = settings;
        _reader = reader;
    }

    public async ValueTask<Result<IReadOnlyList<AiAdminClinicDto>>> Handle(GetAiAdminClinicsQuery request, CancellationToken ct)
    {
        var search = request.Search?.Trim();
        IReadOnlyList<Clinic> clinics;
        if (string.IsNullOrEmpty(search))
        {
            var ids = (await _settings.ListAsync(new AllClinicAiSettingsSpec(), ct)).Select(s => s.ClinicId).Distinct().ToArray();
            clinics = ids.Length == 0 ? [] : await _clinics.ListAsync(new ClinicsForAiAdminSpec(ids), ct);
        }
        else
        {
            if (search.Length < SearchMinLength)
                return Result<IReadOnlyList<AiAdminClinicDto>>.Failure(AiErrors.AdminSearchTooShort);

            var tenantIds = (await _tenants.ListAsync(new TenantsForAiAdminSearchSpec(search, MaxResults), ct)).Select(t => t.Id).ToArray();
            clinics = await _clinics.ListAsync(new ClinicsForAiAdminSearchSpec(search, tenantIds, MaxResults), ct);
        }

        var rows = await _reader.ReadAsync(clinics.ToList(), ct);
        return Result<IReadOnlyList<AiAdminClinicDto>>.Success(rows
            .OrderByDescending(r => r.IsEnabled)
            .ThenBy(r => r.ClinicName, StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true))
            .ToList());
    }
}

/// <summary>
/// Deneme klinikleri için AI'yi açar/kapatır (ADR-044 "az sayıda klinikle deneme"). Yalnız platform yöneticisi, kiracılar
/// arası (maliyet Vetinity'nin API hesabına yazılır). Kayıt yoksa oluşturulur; denetim kaydına yazılır.
/// </summary>
public sealed record SetClinicAiEnabledCommand(Guid ClinicId, bool IsEnabled)
    : IRequest<Result<AiAdminClinicDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Ai.ClinicSettings.Update";
    public string? AuditTarget => $"ClinicId={ClinicId};IsEnabled={IsEnabled}";
}

public sealed class SetClinicAiEnabledCommandValidator : AbstractValidator<SetClinicAiEnabledCommand>
{
    public SetClinicAiEnabledCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class SetClinicAiEnabledCommandHandler : IRequestHandler<SetClinicAiEnabledCommand, Result<AiAdminClinicDto>>
{
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IRepository<ClinicAiSettings> _settings;
    private readonly AiAdminClinicReader _reader;
    private readonly IClientContext _clientContext;
    private readonly TimeProvider _timeProvider;

    public SetClinicAiEnabledCommandHandler(
        IReadRepository<Clinic> clinics,
        IRepository<ClinicAiSettings> settings,
        AiAdminClinicReader reader,
        IClientContext clientContext,
        TimeProvider timeProvider)
    {
        _clinics = clinics;
        _settings = settings;
        _reader = reader;
        _clientContext = clientContext;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AiAdminClinicDto>> Handle(SetClinicAiEnabledCommand request, CancellationToken ct)
    {
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicForAiAdminSpec(request.ClinicId), ct);
        if (clinic is null)
            return Result<AiAdminClinicDto>.Failure(AiErrors.ClinicNotFound);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var row = await _settings.FirstOrDefaultAsync(new ClinicAiSettingsByClinicSpec(clinic.TenantId, clinic.Id), ct);
        if (row is null)
        {
            row = ClinicAiSettings.Create(clinic.TenantId, clinic.Id, utcNow);
            await _settings.AddAsync(row, ct);
        }

        row.SetEnabled(request.IsEnabled, _clientContext.UserId, utcNow);
        await _settings.SaveChangesAsync(ct);
        return Result<AiAdminClinicDto>.Success(await _reader.ReadAsync(clinic, ct));
    }
}

/// <summary>
/// Kliniğe elle AI fonu ekler (ADR-044 madde 6; ücretli satın alma açılana kadar tek yol: deneme, telafi, elden satış).
/// Fon aylık kotadan sonra harcanır ve devreder; geri alınmaz (yanlış eklemede destek kaydıyla düzeltilir). Denetime yazılır.
/// </summary>
public sealed record GrantAiFundCommand(Guid ClinicId, int Credits, string? Note)
    : IRequest<Result<AiAdminClinicDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Ai.Fund.Grant";
    public string? AuditTarget => $"ClinicId={ClinicId};Credits={Credits}";
}

public sealed class GrantAiFundCommandValidator : AbstractValidator<GrantAiFundCommand>
{
    public GrantAiFundCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Credits)
            .InclusiveBetween(1, AiCreditGrant.MaxCredits)
            .WithMessage($"Kredi 1 ile {AiCreditGrant.MaxCredits:N0} arasında olmalıdır.");
        RuleFor(x => x.Note).MaximumLength(AiCreditGrant.NoteMaxLength);
    }
}

public sealed class GrantAiFundCommandHandler : IRequestHandler<GrantAiFundCommand, Result<AiAdminClinicDto>>
{
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IRepository<AiCreditGrant> _grants;
    private readonly AiAdminClinicReader _reader;
    private readonly IClientContext _clientContext;
    private readonly TimeProvider _timeProvider;

    public GrantAiFundCommandHandler(
        IReadRepository<Clinic> clinics,
        IRepository<AiCreditGrant> grants,
        AiAdminClinicReader reader,
        IClientContext clientContext,
        TimeProvider timeProvider)
    {
        _clinics = clinics;
        _grants = grants;
        _reader = reader;
        _clientContext = clientContext;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AiAdminClinicDto>> Handle(GrantAiFundCommand request, CancellationToken ct)
    {
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicForAiAdminSpec(request.ClinicId), ct);
        if (clinic is null)
            return Result<AiAdminClinicDto>.Failure(AiErrors.ClinicNotFound);

        await _grants.AddAsync(AiCreditGrant.CreateManual(
            clinic.TenantId, clinic.Id, request.Credits, request.Note, _clientContext.UserId, _timeProvider.GetUtcNow().UtcDateTime), ct);
        await _grants.SaveChangesAsync(ct);
        return Result<AiAdminClinicDto>.Success(await _reader.ReadAsync(clinic, ct));
    }
}

/// <summary>Platform yönetimi: kliniğin fon hareketi.</summary>
public sealed record AiFundGrantDto(Guid Id, string Kind, int Credits, string? Note, Guid? CreatedByUserId, DateTime CreatedAtUtc);

/// <summary>Kliniğin fon hareketleri (yeniden eskiye, en çok 100).</summary>
public sealed record GetAiFundGrantsQuery(Guid ClinicId) : IRequest<Result<IReadOnlyList<AiFundGrantDto>>>;

public sealed class GetAiFundGrantsQueryHandler : IRequestHandler<GetAiFundGrantsQuery, Result<IReadOnlyList<AiFundGrantDto>>>
{
    public const int MaxResults = 100;

    private readonly IReadRepository<AiCreditGrant> _grants;

    public GetAiFundGrantsQueryHandler(IReadRepository<AiCreditGrant> grants) => _grants = grants;

    public async ValueTask<Result<IReadOnlyList<AiFundGrantDto>>> Handle(GetAiFundGrantsQuery request, CancellationToken ct)
    {
        var grants = await _grants.ListAsync(new AiCreditGrantsByClinicSpec(request.ClinicId, MaxResults), ct);
        return Result<IReadOnlyList<AiFundGrantDto>>.Success(grants
            .Select(g => new AiFundGrantDto(g.Id, g.Kind.ToString(), g.Credits, g.Note, g.CreatedByUserId, g.CreatedAtUtc))
            .ToList());
    }
}

/// <summary>Kırılım satırının ölçüleri: gerçek token, kredi, tahmini maliyet (USD).</summary>
public sealed record AiAdminTotalsDto(
    int Requests,
    int FailedRequests,
    long InputTokens,
    long OutputTokens,
    long Tokens,
    decimal Credits,
    decimal FundCredits,
    decimal CostUsd)
{
    public static AiAdminTotalsDto From(AiUsageStats s)
        => new(s.Requests, s.FailedRequests, s.InputTokens, s.OutputTokens, s.Tokens, s.Credits, s.FundCredits, decimal.Round(s.CostUsd, 6));
}

/// <summary>Sistem aylık harcama sınırına uzaklık (ADR-044 madde 8).</summary>
public sealed record AiAdminBudgetDto(decimal LimitUsd, decimal SpentUsd, decimal RemainingUsd, int UsedPercent);

public sealed record AiAdminLimitsDto(
    int MaxInputTokens,
    int MaxOutputTokens,
    int UserRequestsPerMinute,
    long ClinicMonthlyTokenLimit,
    decimal ClinicMonthlyCostLimitUsd,
    decimal SystemMonthlyCostLimitUsd,
    decimal CreditValueUsd,
    IReadOnlyDictionary<string, int> PlanMonthlyCredits);

public sealed record AiAdminClinicUsageDto(
    Guid TenantId, string TenantName, Guid ClinicId, string ClinicName, bool IsEnabled, AiAdminTotalsDto Usage);

public sealed record AiAdminFeatureUsageDto(string Feature, AiAdminTotalsDto Usage);

public sealed record AiAdminModelUsageDto(string Model, AiAdminTotalsDto Usage);

/// <summary>Kullanıcı kırılımı (kötüye kullanım takibi): ayın en çok harcayan kullanıcıları, klinik başına ayrı satır.</summary>
public sealed record AiAdminUserUsageDto(
    Guid UserId, string UserName, string Email, Guid TenantId, string TenantName, Guid ClinicId, string ClinicName, AiAdminTotalsDto Usage);

/// <summary>Platform yönetimi (AI-008): ayın kullanımı ve maliyeti; maliyet ayardaki fiyatla tahmindir, fatura değildir.</summary>
/// <param name="Month">İstanbul takvim ayı, <c>yyyy-MM</c>.</param>
public sealed record AiAdminUsageDto(
    bool IsConfigured,
    string Model,
    string Month,
    bool IsCurrentMonth,
    DateTime MonthStartUtc,
    DateTime MonthEndUtc,
    AiAdminTotalsDto System,
    AiAdminBudgetDto Budget,
    AiAdminLimitsDto Limits,
    IReadOnlyList<AiAdminClinicUsageDto> Clinics,
    IReadOnlyList<AiAdminFeatureUsageDto> Features,
    IReadOnlyList<AiAdminModelUsageDto> Models,
    IReadOnlyList<AiAdminUserUsageDto> Users);

/// <param name="Month"><c>yyyy-MM</c> (İstanbul takvim ayı); boşsa bu ay.</param>
public sealed record GetAiAdminUsageQuery(string? Month = null) : IRequest<Result<AiAdminUsageDto>>;

public sealed class GetAiAdminUsageQueryValidator : AbstractValidator<GetAiAdminUsageQuery>
{
    public GetAiAdminUsageQueryValidator()
        => RuleFor(x => x.Month)
            .Must(m => GetAiAdminUsageQueryHandler.TryParseMonth(m, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Month))
            .WithMessage("Ay yyyy-MM biçiminde olmalıdır.");
}

public sealed class GetAiAdminUsageQueryHandler : IRequestHandler<GetAiAdminUsageQuery, Result<AiAdminUsageDto>>
{
    private readonly AiOptions _options;
    private readonly AiUsageGuard _guard;
    private readonly IAiUsageReader _usage;
    private readonly IReadRepository<ClinicAiSettings> _settings;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TimeProvider _timeProvider;

    public GetAiAdminUsageQueryHandler(
        IOptions<AiOptions> options,
        AiUsageGuard guard,
        IAiUsageReader usage,
        IReadRepository<ClinicAiSettings> settings,
        IReadRepository<Clinic> clinics,
        IReadRepository<Tenant> tenants,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _guard = guard;
        _usage = usage;
        _settings = settings;
        _clinics = clinics;
        _tenants = tenants;
        _timeProvider = timeProvider;
    }

    /// <summary><c>yyyy-MM</c> → ayın ortası (UTC); İstanbul ay sınırı buradan hesaplanır.</summary>
    public static bool TryParseMonth(string? value, out DateTime midMonthUtc)
    {
        midMonthUtc = default;
        if (!DateTime.TryParseExact(value?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            || month.Year < 2000)
            return false;

        midMonthUtc = new DateTime(month.Year, month.Month, 15, 12, 0, 0, DateTimeKind.Utc);
        return true;
    }

    public async ValueTask<Result<AiAdminUsageDto>> Handle(GetAiAdminUsageQuery request, CancellationToken ct)
    {
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var current = OperationPeriodBounds.MonthForUtcNow(utcNow);
        var (monthStart, monthEnd) = TryParseMonth(request.Month, out var mid) ? OperationPeriodBounds.MonthForUtcNow(mid) : current;
        var isCurrent = monthStart == current.StartUtcInclusive;

        var breakdown = await _usage.BreakdownAsync(monthStart, monthEnd, ct);
        var enabledIds = (await _settings.ListAsync(new EnabledClinicAiSettingsSpec(), ct)).Select(s => s.ClinicId).ToHashSet();

        var clinics = breakdown.Clinics
            .Select(r => new AiAdminClinicUsageDto(
                r.TenantId, r.TenantName, r.ClinicId, r.ClinicName, enabledIds.Contains(r.ClinicId), AiAdminTotalsDto.From(r.Stats)))
            .ToList();

        // Bu ay: açık olup kullanmayan klinikler de listelenir (denemedeki klinikler tek bakışta görünsün).
        var missingIds = isCurrent ? enabledIds.Except(clinics.Select(c => c.ClinicId)).ToArray() : [];
        if (missingIds.Length > 0)
        {
            var missing = await _clinics.ListAsync(new ClinicsForAiAdminSpec(missingIds), ct);
            var tenantIds = missing.Select(c => c.TenantId).Distinct().ToArray();
            var tenantNames = (await _tenants.ListAsync(new TenantsForAiAdminSpec(tenantIds), ct)).ToDictionary(t => t.Id, t => t.Name);
            clinics.AddRange(missing.Select(c => new AiAdminClinicUsageDto(
                c.TenantId, tenantNames.GetValueOrDefault(c.TenantId, string.Empty), c.Id, c.Name, true, AiAdminTotalsDto.From(AiUsageStats.Zero))));
        }

        var spent = breakdown.Total.CostUsd;
        var limit = _options.SystemMonthlyCostLimitUsd;
        var budget = new AiAdminBudgetDto(
            limit,
            decimal.Round(spent, 6),
            decimal.Round(Math.Max(0m, limit - spent), 6),
            limit > 0 ? (int)Math.Min(100m, Math.Floor(spent / limit * 100m)) : 100);

        return Result<AiAdminUsageDto>.Success(new AiAdminUsageDto(
            _guard.IsConfigured,
            _options.Model,
            OperationDayBounds.ToLocal(monthStart).ToString("yyyy-MM", CultureInfo.InvariantCulture),
            isCurrent,
            monthStart,
            monthEnd,
            AiAdminTotalsDto.From(breakdown.Total),
            budget,
            new AiAdminLimitsDto(
                _options.MaxInputTokens,
                _options.MaxOutputTokens,
                _options.UserRequestsPerMinute,
                _options.ClinicMonthlyTokenLimit,
                _options.ClinicMonthlyCostLimitUsd,
                _options.SystemMonthlyCostLimitUsd,
                _options.CreditValueUsd,
                new Dictionary<string, int>(_options.PlanMonthlyCredits, StringComparer.OrdinalIgnoreCase)),
            clinics,
            breakdown.Features.Select(f => new AiAdminFeatureUsageDto(f.Feature.ToString(), AiAdminTotalsDto.From(f.Stats))).ToList(),
            breakdown.Models.Select(m => new AiAdminModelUsageDto(m.Model, AiAdminTotalsDto.From(m.Stats))).ToList(),
            breakdown.Users
                .Select(u => new AiAdminUserUsageDto(
                    u.UserId, u.UserName, u.Email, u.TenantId, u.TenantName, u.ClinicId, u.ClinicName, AiAdminTotalsDto.From(u.Stats)))
                .ToList()));
    }
}
