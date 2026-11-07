using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Reminders.Rules;

/// <summary>
/// Kliniğin otomatik hatırlatıcı kuralı (TIER-003; docs/AUTOMATED_REMINDERS_API_CONTRACT.md). Kayıt yoksa kapalı ve varsayılan gün.
/// <see cref="SendingAvailable"/> <c>false</c>: ticari ileti gönderimi merkezi olarak kapalı, kural açılsa da e-posta gitmez.
/// </summary>
public sealed record ClinicReminderRuleDto(
    string RuleType,
    bool IsEnabled,
    int Days,
    int DefaultDays,
    int MinDays,
    int MaxDays,
    bool SendingAvailable,
    DateTime? UpdatedAtUtc);

public sealed record ClinicReminderRulesDto(Guid ClinicId, IReadOnlyList<ClinicReminderRuleDto> Rules);

public sealed record GetClinicReminderRulesQuery(Guid ClinicId) : IRequest<Result<ClinicReminderRulesDto>>;

public sealed record ClinicReminderRuleUpdate(string? RuleType, bool IsEnabled, int Days);

/// <summary>Listede olmayan kural değişmez. Açmak paket ister; kapatmak ve gün değiştirmek paket düşünce de serbesttir.</summary>
public sealed record UpdateClinicReminderRulesCommand(Guid ClinicId, IReadOnlyList<ClinicReminderRuleUpdate>? Rules)
    : IRequest<Result<ClinicReminderRulesDto>>, IAuditableRequest
{
    public string AuditAction => "Reminders.Rules.Update";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

public sealed class ClinicReminderRulesByClinicSpec : Specification<ClinicReminderRule>
{
    public ClinicReminderRulesByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(r => r.TenantId == tenantId && r.ClinicId == clinicId);
}

public sealed class GetClinicReminderRulesQueryValidator : AbstractValidator<GetClinicReminderRulesQuery>
{
    public GetClinicReminderRulesQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class UpdateClinicReminderRulesCommandValidator : AbstractValidator<UpdateClinicReminderRulesCommand>
{
    public UpdateClinicReminderRulesCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Rules).NotEmpty();
        RuleFor(x => x.Rules)
            .Must(rules => rules!.Select(r => r.RuleType?.Trim().ToLowerInvariant()).Distinct().Count() == rules!.Count)
            .When(x => x.Rules is { Count: > 0 })
            .WithMessage("Aynı kural iki kez gönderilemez.");
        RuleForEach(x => x.Rules).ChildRules(rule =>
            rule.RuleFor(r => r.RuleType)
                .Must(t => ClinicReminderRuleMapping.TryParse(t, out _))
                .WithMessage("Geçersiz kural türü."));
    }
}

internal static class ClinicReminderRuleMapping
{
    public static bool TryParse(string? value, out ClinicReminderRuleType type)
        => Enum.TryParse(value?.Trim(), ignoreCase: true, out type) && Enum.IsDefined(type);

    /// <summary>Tüm kurallar katalog sırasıyla döner; kaydı olmayan kural kapalı ve varsayılan gündedir.</summary>
    public static ClinicReminderRulesDto ToDto(Guid clinicId, IReadOnlyCollection<ClinicReminderRule> rows)
        => new(
            clinicId,
            ClinicReminderRuleDefinition.All
                .Select(d =>
                {
                    var row = rows.FirstOrDefault(r => r.RuleType == d.Type);
                    return new ClinicReminderRuleDto(
                        d.Type.ToString(),
                        row?.IsEnabled ?? false,
                        row?.Days ?? d.DefaultDays,
                        d.DefaultDays,
                        d.MinDays,
                        d.MaxDays,
                        d.SendingAvailable,
                        row?.UpdatedAtUtc);
                })
                .ToArray());
}

public sealed class GetClinicReminderRulesQueryHandler : IRequestHandler<GetClinicReminderRulesQuery, Result<ClinicReminderRulesDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicReminderRule> _rules;

    public GetClinicReminderRulesQueryHandler(ClinicDefinitionAccess access, IReadRepository<ClinicReminderRule> rules)
    {
        _access = access;
        _rules = rules;
    }

    public async ValueTask<Result<ClinicReminderRulesDto>> Handle(GetClinicReminderRulesQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicReminderRulesDto>.Failure(access.Error);

        var rows = await _rules.ListAsync(new ClinicReminderRulesByClinicSpec(access.Value, request.ClinicId), ct);
        return Result<ClinicReminderRulesDto>.Success(ClinicReminderRuleMapping.ToDto(request.ClinicId, rows));
    }
}

public sealed class UpdateClinicReminderRulesCommandHandler : IRequestHandler<UpdateClinicReminderRulesCommand, Result<ClinicReminderRulesDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ClinicReminderRule> _rules;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicReminderRulesCommandHandler(
        ClinicDefinitionAccess access,
        IClientContext clientContext,
        IRepository<ClinicReminderRule> rules,
        TenantPlanFeatureEvaluator planFeatures,
        TimeProvider timeProvider)
    {
        _access = access;
        _clientContext = clientContext;
        _rules = rules;
        _planFeatures = planFeatures;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicReminderRulesDto>> Handle(UpdateClinicReminderRulesCommand request, CancellationToken ct)
    {
        // Yetki rotada Reminders.Manage'dir; burada klinik kapsamı denetlenir (Clinics.Update istenmez).
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicReminderRulesDto>.Failure(access.Error);
        var tenantId = access.Value;

        if (_clientContext.UserId is not { } userId)
            return Result<ClinicReminderRulesDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var updates = request.Rules!;
        if (updates.Any(u => u.IsEnabled))
        {
            var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.AutomatedReminders, ct);
            if (!planAllows.IsSuccess)
                return Result<ClinicReminderRulesDto>.Failure(planAllows.Error);
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var rows = (await _rules.ListAsync(new ClinicReminderRulesByClinicSpec(tenantId, request.ClinicId), ct)).ToList();

        foreach (var update in updates)
        {
            ClinicReminderRuleMapping.TryParse(update.RuleType, out var type);
            var row = rows.FirstOrDefault(r => r.RuleType == type);
            var isNew = row is null;
            row ??= ClinicReminderRule.CreateDisabled(tenantId, request.ClinicId, type, utcNow);

            var applied = row.Update(update.IsEnabled, update.Days, userId, utcNow);
            if (!applied.IsSuccess)
                return Result<ClinicReminderRulesDto>.Failure(applied.Error);

            if (isNew)
            {
                await _rules.AddAsync(row, ct);
                rows.Add(row);
            }
        }

        await _rules.SaveChangesAsync(ct);
        return Result<ClinicReminderRulesDto>.Success(ClinicReminderRuleMapping.ToDto(request.ClinicId, rows));
    }
}
