using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Ai;

/// <summary>Platform yönetimi: kliniğin AI durumu (docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §3).</summary>
public sealed record AiAdminClinicDto(Guid TenantId, Guid ClinicId, string ClinicName, bool IsEnabled, DateTime? UpdatedAtUtc);

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
    private readonly IClientContext _clientContext;
    private readonly TimeProvider _timeProvider;

    public SetClinicAiEnabledCommandHandler(
        IReadRepository<Clinic> clinics,
        IRepository<ClinicAiSettings> settings,
        IClientContext clientContext,
        TimeProvider timeProvider)
    {
        _clinics = clinics;
        _settings = settings;
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
        return Result<AiAdminClinicDto>.Success(new AiAdminClinicDto(clinic.TenantId, clinic.Id, clinic.Name, row.IsEnabled, row.UpdatedAtUtc));
    }
}

/// <summary>Platform yönetimi: bu ayın AI kullanımı (sistem toplamı, sınırlar, klinik bazında; maliyet tahminidir).</summary>
public sealed record AiAdminUsageDto(
    bool IsConfigured,
    string Model,
    DateTime MonthStartUtc,
    DateTime MonthEndUtc,
    AiAdminTotalsDto System,
    AiAdminLimitsDto Limits,
    IReadOnlyList<AiAdminClinicUsageDto> Clinics);

public sealed record AiAdminTotalsDto(int Requests, long Tokens, decimal CostUsd);

public sealed record AiAdminLimitsDto(
    int MaxInputTokens,
    int MaxOutputTokens,
    int UserRequestsPerMinute,
    long ClinicMonthlyTokenLimit,
    decimal ClinicMonthlyCostLimitUsd,
    decimal SystemMonthlyCostLimitUsd);

public sealed record AiAdminClinicUsageDto(
    Guid TenantId, Guid ClinicId, string ClinicName, bool IsEnabled, int Requests, long Tokens, decimal CostUsd);

public sealed record GetAiAdminUsageQuery : IRequest<Result<AiAdminUsageDto>>;

public sealed class GetAiAdminUsageQueryHandler : IRequestHandler<GetAiAdminUsageQuery, Result<AiAdminUsageDto>>
{
    private readonly AiOptions _options;
    private readonly AiUsageGuard _guard;
    private readonly IAiUsageReader _usage;
    private readonly IReadRepository<ClinicAiSettings> _settings;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly TimeProvider _timeProvider;

    public GetAiAdminUsageQueryHandler(
        IOptions<AiOptions> options,
        AiUsageGuard guard,
        IAiUsageReader usage,
        IReadRepository<ClinicAiSettings> settings,
        IReadRepository<Clinic> clinics,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _guard = guard;
        _usage = usage;
        _settings = settings;
        _clinics = clinics;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AiAdminUsageDto>> Handle(GetAiAdminUsageQuery request, CancellationToken ct)
    {
        var (monthStart, monthEnd) = OperationPeriodBounds.MonthForUtcNow(_timeProvider.GetUtcNow().UtcDateTime);
        var system = await _usage.SystemTotalsAsync(monthStart, monthEnd, ct);
        var usageRows = await _usage.ClinicBreakdownAsync(monthStart, monthEnd, ct);
        var enabled = await _settings.ListAsync(new EnabledClinicAiSettingsSpec(), ct);

        // Açık olup bu ay kullanmayan klinikler de listelenir (denemedeki klinikler tek bakışta görünsün).
        var enabledIds = enabled.Select(s => s.ClinicId).ToHashSet();
        var missingIds = enabledIds.Except(usageRows.Select(r => r.ClinicId)).ToArray();
        var missingClinics = missingIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsForAiAdminSpec(missingIds), ct);

        var clinics = usageRows
            .Select(r => new AiAdminClinicUsageDto(r.TenantId, r.ClinicId, r.ClinicName, enabledIds.Contains(r.ClinicId), r.Requests, r.Tokens, r.CostUsd))
            .Concat(missingClinics.Select(c => new AiAdminClinicUsageDto(c.TenantId, c.Id, c.Name, true, 0, 0, 0m)))
            .ToList();

        return Result<AiAdminUsageDto>.Success(new AiAdminUsageDto(
            _guard.IsConfigured,
            _options.Model,
            monthStart,
            monthEnd,
            new AiAdminTotalsDto(system.Requests, system.Tokens, system.CostUsd),
            new AiAdminLimitsDto(
                _options.MaxInputTokens,
                _options.MaxOutputTokens,
                _options.UserRequestsPerMinute,
                _options.ClinicMonthlyTokenLimit,
                _options.ClinicMonthlyCostLimitUsd,
                _options.SystemMonthlyCostLimitUsd),
            clinics));
    }
}
