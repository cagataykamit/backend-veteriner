using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Finance;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.CashCounts;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.CashCounts;

/// <summary>Kasa sayımı kaydı; ad <c>DisplayName</c> yoksa e-postadan türetilir.</summary>
public sealed record CashCountDto(
    Guid Id,
    Guid ClinicId,
    DateOnly BusinessDate,
    string Currency,
    decimal OpeningAmount,
    decimal ExpectedCashAmount,
    decimal ExpectedClosingAmount,
    decimal CountedAmount,
    decimal DifferenceAmount,
    string? Note,
    Guid? CorrectsCashCountId,
    Guid CreatedByUserId,
    string? CreatedByName,
    DateTime CreatedAtUtc,
    bool IsVoided,
    DateTime? VoidedAtUtc,
    Guid? VoidedByUserId,
    string? VoidedByName,
    string? VoidReason);

public sealed record CashCountPreviousDto(DateOnly BusinessDate, decimal CountedAmount);

/// <summary>
/// Günün kasa kontrolü. <c>Cash</c> gün sonu kasa özetinin nakit satırıdır (şu anki değer); <c>Current</c> günün geçerli sayımıdır ve
/// beklenen nakdi kayıt anındaki değerle taşır. İkisi farklıysa (<c>ExpectedChangedSinceCount</c>) sayımdan sonra nakit hareketi
/// değişmiştir. <c>History</c> günün tüm kayıtları (iptal ve düzeltilenler dahil), yeniden eskiye.
/// </summary>
public sealed record CashCountDayDto(
    Guid ClinicId,
    DateOnly BusinessDate,
    string Currency,
    CashSummaryMethodDto Cash,
    CashCountDto? Current,
    bool ExpectedChangedSinceCount,
    CashCountPreviousDto? Previous,
    IReadOnlyList<CashCountDto> History);

/// <summary>GET /api/v1/cash-counts/day. Tek klinik: <c>clinicId</c> ya da aktif klinik bağlamı.</summary>
public sealed record GetCashCountDayQuery(DateOnly Date, Guid? ClinicId) : IRequest<Result<CashCountDayDto>>;

/// <summary>GET /api/v1/cash-counts. Gün azalan, sonra kayıt zamanı azalan; varsayılan yalnız geçerli sayımlar.</summary>
public sealed record GetCashCountsQuery(DateOnly From, DateOnly To, Guid? ClinicId, bool IncludeVoided, int Page, int PageSize)
    : IRequest<Result<PagedResult<CashCountDto>>>;

/// <summary>
/// POST /api/v1/cash-counts. <see cref="ExpectedCashAmount"/> istemcinin gösterdiği beklenen nakittir; sunucunun hesabıyla aynı değilse
/// kayıt yazılmaz (<c>CashCounts.ExpectedChanged</c>), personel güncel rakamı görüp yeniden onaylar.
/// </summary>
public sealed record CreateCashCountCommand(
    Guid? ClinicId,
    DateOnly BusinessDate,
    decimal OpeningAmount,
    decimal CountedAmount,
    decimal ExpectedCashAmount,
    string? Note)
    : IRequest<Result<CashCountDto>>, IAuditableRequest
{
    public string AuditAction => "CashCount.Create";
    public string? AuditTarget => $"ClinicId={ClinicId};BusinessDate={BusinessDate:yyyy-MM-dd}";
}

/// <summary>
/// POST /api/v1/cash-counts/{id}/correct. Eski kayıt gerekçeyle iptal edilir, yerine bağlı yeni kayıt döner; ikisi tek transaction'da
/// (yeni kayıt yazılamazsa iptal de geri alınır).
/// </summary>
public sealed record CorrectCashCountCommand(
    Guid Id,
    decimal OpeningAmount,
    decimal CountedAmount,
    decimal ExpectedCashAmount,
    string? Note,
    string? Reason)
    : IRequest<Result<CashCountDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "CashCount.Correct";
    public string? AuditTarget => $"CashCountId={Id}";
}

/// <summary>POST /api/v1/cash-counts/{id}/void. Gün yeniden sayılabilir hale gelir.</summary>
public sealed record VoidCashCountCommand(Guid Id, string? Reason) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "CashCount.Void";
    public string? AuditTarget => $"CashCountId={Id}";
}

public sealed class GetCashCountsQueryValidator : AbstractValidator<GetCashCountsQuery>
{
    public GetCashCountsQueryValidator() => RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
}

public sealed class CorrectCashCountCommandValidator : AbstractValidator<CorrectCashCountCommand>
{
    public CorrectCashCountCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class VoidCashCountCommandValidator : AbstractValidator<VoidCashCountCommand>
{
    public VoidCashCountCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class CashCountsForDaySpec : Specification<CashCount>
{
    public CashCountsForDaySpec(Guid tenantId, Guid clinicId, DateOnly date)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && c.BusinessDate == date);
        Query.OrderByDescending(c => c.CreatedAtUtc);
    }
}

public sealed class CashCountPreviousSpec : Specification<CashCount, CashCountPreviousDto>
{
    public CashCountPreviousSpec(Guid tenantId, Guid clinicId, DateOnly before)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && c.BusinessDate < before && c.VoidedAtUtc == null);
        Query.OrderByDescending(c => c.BusinessDate);
        Query.Select(c => new CashCountPreviousDto(c.BusinessDate, c.CountedAmount));
    }
}

public sealed class CashCountsFilteredSpec : Specification<CashCount>
{
    public CashCountsFilteredSpec(Guid tenantId, Guid clinicId, DateOnly from, DateOnly to, bool includeVoided, int? skip = null, int? take = null)
    {
        Query.AsNoTracking();
        Query.Where(c => c.TenantId == tenantId && c.ClinicId == clinicId && c.BusinessDate >= from && c.BusinessDate <= to);
        if (!includeVoided)
            Query.Where(c => c.VoidedAtUtc == null);
        Query.OrderByDescending(c => c.BusinessDate).ThenByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id);
        if (skip is { } s && take is { } t)
            Query.Skip(s).Take(t);
    }
}

public sealed class CashCountForUpdateSpec : Specification<CashCount>
{
    public CashCountForUpdateSpec(Guid tenantId, Guid id) => Query.Where(c => c.TenantId == tenantId && c.Id == id);
}

public sealed record CashCountUser(Guid Id, string? DisplayName, string Email);

public sealed class CashCountUsersSpec : Specification<User, CashCountUser>
{
    public CashCountUsersSpec(IReadOnlyCollection<Guid> userIds)
    {
        Query.AsNoTracking();
        Query.Where(u => userIds.Contains(u.Id));
        Query.Select(u => new CashCountUser(u.Id, u.DisplayName, u.Email));
    }
}

/// <summary>
/// Sayımın ortak kuralları tek yerde: klinik-gün kapsamı (gün sonu raporlarıyla aynı çözümleyici) ve beklenen nakit. Beklenen nakit
/// gün sonu kasa özetinin nakit satırının netidir (<see cref="CashSummaryLoader"/>); ayrı hesap yoktur.
/// </summary>
internal static class CashCountRules
{
    public const string ExpectedChangedCode = "CashCounts.ExpectedChanged";

    public static async Task<Result<FinanceEventFilter>> ResolveClinicAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        DateOnly from,
        DateOnly to,
        Guid? clinicId,
        CancellationToken ct)
    {
        var filter = await DailyTransactionsReportFilter.ResolveAsync(
            tenantContext, clinicContext, scopeResolver, from, to, clinicId, null, null, null, ct);
        if (!filter.IsSuccess)
            return filter;

        return filter.Value!.ClinicId is null
            ? Result<FinanceEventFilter>.Failure(
                "CashCounts.ClinicScopeRequired",
                "Kasa sayımı için klinik belirlenemedi (clinicId veya aktif klinik bağlamı gerekir).")
            : filter;
    }

    public static async Task<Result<CashSummaryMethodDto>> LoadCashAsync(
        ICashEventsReader reader, FinanceEventFilter dayFilter, DateOnly date, CancellationToken ct)
    {
        var summary = await CashSummaryLoader.LoadAsync(reader, dayFilter, date, date, ct);
        return summary.IsSuccess
            ? Result<CashSummaryMethodDto>.Success(summary.Value!.ByMethod.Single(m => m.PaymentMethod == PaymentMethod.Cash))
            : Result<CashSummaryMethodDto>.Failure(summary.Error);
    }

    public static Result EnsureExpectedUnchanged(decimal shown, decimal current)
        => shown == current
            ? Result.Success()
            : Result.Failure(
                ExpectedChangedCode,
                $"Günün nakit hareketi değişti (beklenen nakit şimdi {current:0.00}); güncel rakamı kontrol edip yeniden kaydedin.");

    public static DateOnly Today(TimeProvider timeProvider) => OperationDayBounds.ToLocalDate(timeProvider.GetUtcNow().UtcDateTime);

    public static async Task<IReadOnlyList<CashCountDto>> ToDtosAsync(
        IReadRepository<User> users, IReadOnlyList<CashCount> counts, CancellationToken ct)
    {
        var ids = counts.Select(c => c.CreatedByUserId)
            .Concat(counts.Where(c => c.VoidedByUserId.HasValue).Select(c => c.VoidedByUserId!.Value))
            .Distinct()
            .ToArray();
        var names = ids.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await users.ListAsync(new CashCountUsersSpec(ids), ct)).ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));

        return counts.Select(c => ToDto(c, names)).ToList();
    }

    private static CashCountDto ToDto(CashCount c, IReadOnlyDictionary<Guid, string?> names)
        => new(
            c.Id, c.ClinicId, c.BusinessDate, c.Currency,
            c.OpeningAmount, c.ExpectedCashAmount, c.ExpectedClosingAmount, c.CountedAmount, c.DifferenceAmount,
            c.Note, c.CorrectsCashCountId,
            c.CreatedByUserId, names.GetValueOrDefault(c.CreatedByUserId), c.CreatedAtUtc,
            c.IsVoided, c.VoidedAtUtc, c.VoidedByUserId, c.VoidedByUserId is { } v ? names.GetValueOrDefault(v) : null, c.VoidReason);
}

public sealed class GetCashCountDayQueryHandler : IRequestHandler<GetCashCountDayQuery, Result<CashCountDayDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICashEventsReader _cashEvents;
    private readonly IReadRepository<CashCount> _counts;
    private readonly IReadRepository<User> _users;

    public GetCashCountDayQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICashEventsReader cashEvents,
        IReadRepository<CashCount> counts,
        IReadRepository<User> users)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _cashEvents = cashEvents;
        _counts = counts;
        _users = users;
    }

    public async ValueTask<Result<CashCountDayDto>> Handle(GetCashCountDayQuery request, CancellationToken ct)
    {
        var filter = await CashCountRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, request.Date, request.Date, request.ClinicId, ct);
        if (!filter.IsSuccess)
            return Result<CashCountDayDto>.Failure(filter.Error);
        var f = filter.Value!;
        var clinicId = f.ClinicId!.Value;

        var cash = await CashCountRules.LoadCashAsync(_cashEvents, f, request.Date, ct);
        if (!cash.IsSuccess)
            return Result<CashCountDayDto>.Failure(cash.Error);

        var dayCounts = await _counts.ListAsync(new CashCountsForDaySpec(f.TenantId, clinicId, request.Date), ct);
        var history = await CashCountRules.ToDtosAsync(_users, dayCounts, ct);
        var current = history.FirstOrDefault(c => !c.IsVoided);
        var previous = await _counts.FirstOrDefaultAsync(new CashCountPreviousSpec(f.TenantId, clinicId, request.Date), ct);

        return Result<CashCountDayDto>.Success(new CashCountDayDto(
            clinicId,
            request.Date,
            CashCount.DefaultCurrency,
            cash.Value!,
            current,
            current is not null && current.ExpectedCashAmount != cash.Value!.NetAmount,
            previous,
            history));
    }
}

public sealed class GetCashCountsQueryHandler : IRequestHandler<GetCashCountsQuery, Result<PagedResult<CashCountDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<CashCount> _counts;
    private readonly IReadRepository<User> _users;

    public GetCashCountsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<CashCount> counts,
        IReadRepository<User> users)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _counts = counts;
        _users = users;
    }

    public async ValueTask<Result<PagedResult<CashCountDto>>> Handle(GetCashCountsQuery request, CancellationToken ct)
    {
        var filter = await CashCountRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, request.From, request.To, request.ClinicId, ct);
        if (!filter.IsSuccess)
            return Result<PagedResult<CashCountDto>>.Failure(filter.Error);
        var f = filter.Value!;
        var clinicId = f.ClinicId!.Value;

        var pageSize = Math.Clamp(request.PageSize, 1, RegisterReportConstants.MaxPageSize);
        var total = await _counts.CountAsync(
            new CashCountsFilteredSpec(f.TenantId, clinicId, request.From, request.To, request.IncludeVoided), ct);
        var rows = await _counts.ListAsync(
            new CashCountsFilteredSpec(
                f.TenantId, clinicId, request.From, request.To, request.IncludeVoided, (request.Page - 1) * pageSize, pageSize),
            ct);

        var items = await CashCountRules.ToDtosAsync(_users, rows, ct);
        return Result<PagedResult<CashCountDto>>.Success(PagedResult<CashCountDto>.Create(items, total, request.Page, pageSize));
    }
}

public sealed class CreateCashCountCommandHandler : IRequestHandler<CreateCashCountCommand, Result<CashCountDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly ICashEventsReader _cashEvents;
    private readonly IRepository<CashCount> _counts;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public CreateCashCountCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        ICashEventsReader cashEvents,
        IRepository<CashCount> counts,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _cashEvents = cashEvents;
        _counts = counts;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashCountDto>> Handle(CreateCashCountCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashCountDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashCountDto>.Failure(planAllows.Error);

        var filter = await CashCountRules.ResolveClinicAsync(
            _tenantContext, _clinicContext, _scopeResolver, request.BusinessDate, request.BusinessDate, request.ClinicId, ct);
        if (!filter.IsSuccess)
            return Result<CashCountDto>.Failure(filter.Error);
        var clinicId = filter.Value!.ClinicId!.Value;

        var cash = await CashCountRules.LoadCashAsync(_cashEvents, filter.Value!, request.BusinessDate, ct);
        if (!cash.IsSuccess)
            return Result<CashCountDto>.Failure(cash.Error);
        var unchanged = CashCountRules.EnsureExpectedUnchanged(request.ExpectedCashAmount, cash.Value!.NetAmount);
        if (!unchanged.IsSuccess)
            return Result<CashCountDto>.Failure(unchanged.Error);

        var created = CashCount.Create(
            tenantId, clinicId, request.BusinessDate, CashCountRules.Today(_timeProvider),
            request.OpeningAmount, cash.Value!.NetAmount, request.CountedAmount, request.Note,
            userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<CashCountDto>.Failure(created.Error);

        await _counts.AddAsync(created.Value!, ct);
        try
        {
            await _counts.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (CashCountErrors.IsActivePerDayViolation(ex))
        {
            return Result<CashCountDto>.Failure(CashCountErrors.DuplicateForDay);
        }

        return Result<CashCountDto>.Success((await CashCountRules.ToDtosAsync(_users, [created.Value!], ct))[0]);
    }
}

public sealed class CorrectCashCountCommandHandler : IRequestHandler<CorrectCashCountCommand, Result<CashCountDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly ICashEventsReader _cashEvents;
    private readonly IRepository<CashCount> _counts;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public CorrectCashCountCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        ICashEventsReader cashEvents,
        IRepository<CashCount> counts,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _cashEvents = cashEvents;
        _counts = counts;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CashCountDto>> Handle(CorrectCashCountCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result<CashCountDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return Result<CashCountDto>.Failure(planAllows.Error);

        var count = await CashCountErrors.FindInScopeAsync(_counts, _clinicContext, _scopeResolver, tenantId, request.Id, ct);
        if (!count.IsSuccess)
            return Result<CashCountDto>.Failure(count.Error);
        var original = count.Value!;

        var dayFilter = DailyTransactionsReportFilter.ForScope(tenantId, original.ClinicId, null, original.BusinessDate, original.BusinessDate);
        var cash = await CashCountRules.LoadCashAsync(_cashEvents, dayFilter, original.BusinessDate, ct);
        if (!cash.IsSuccess)
            return Result<CashCountDto>.Failure(cash.Error);
        var unchanged = CashCountRules.EnsureExpectedUnchanged(request.ExpectedCashAmount, cash.Value!.NetAmount);
        if (!unchanged.IsSuccess)
            return Result<CashCountDto>.Failure(unchanged.Error);

        var replacement = original.Correct(
            request.OpeningAmount, cash.Value!.NetAmount, request.CountedAmount, request.Note, request.Reason,
            userId, CashCountRules.Today(_timeProvider), _timeProvider.GetUtcNow().UtcDateTime);
        if (!replacement.IsSuccess)
            return Result<CashCountDto>.Failure(replacement.Error);

        // Önce iptal yazılır ki gün başına tek geçerli kayıt indeksi yeni kaydı kabul etsin; ikisi aynı transaction'dadır.
        try
        {
            await _counts.SaveChangesAsync(ct);
            await _counts.AddAsync(replacement.Value!, ct);
            await _counts.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<CashCountDto>.Failure(CashCountErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (CashCountErrors.IsActivePerDayViolation(ex))
        {
            return Result<CashCountDto>.Failure(CashCountErrors.ConcurrencyConflict);
        }

        return Result<CashCountDto>.Success((await CashCountRules.ToDtosAsync(_users, [replacement.Value!], ct))[0]);
    }
}

public sealed class VoidCashCountCommandHandler : IRequestHandler<VoidCashCountCommand, Result>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CashCount> _counts;
    private readonly TimeProvider _timeProvider;

    public VoidCashCountCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver scopeResolver,
        IReadRepository<Tenant> tenants,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CashCount> counts,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _scopeResolver = scopeResolver;
        _tenants = tenants;
        _planFeatures = planFeatures;
        _counts = counts;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(VoidCashCountCommand request, CancellationToken ct)
    {
        var actor = await FinanceCorrectionActor.ResolveAsync(_tenantContext, _clientContext, _tenants, ct);
        if (!actor.IsSuccess)
            return Result.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
        if (!planAllows.IsSuccess)
            return planAllows;

        var count = await CashCountErrors.FindInScopeAsync(_counts, _clinicContext, _scopeResolver, tenantId, request.Id, ct);
        if (!count.IsSuccess)
            return Result.Failure(count.Error);

        var voided = count.Value!.Void(userId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!voided.IsSuccess)
            return voided;

        try
        {
            await _counts.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(CashCountErrors.ConcurrencyConflict);
        }

        return Result.Success();
    }
}

public static class CashCountErrors
{
    /// <summary>Klinik ve gün başına tek geçerli sayım indeksi (EF yapılandırması bu adı kullanır).</summary>
    public const string ActivePerDayIndexName = "UX_CashCounts_TenantId_ClinicId_BusinessDate_Active";

    internal static readonly Error DuplicateForDay = new(
        "CashCounts.DuplicateForDay", "Bu klinik ve gün için kasa sayımı zaten kaydedilmiş; değiştirmek için düzeltme yapın.");

    internal static readonly Error ConcurrencyConflict = new(
        "CashCounts.ConcurrencyConflict", "Kasa sayımı eşzamanlı olarak değişti; yeniden okunup işlem tekrarlanmalı.");

    internal static bool IsActivePerDayViolation(DbUpdateException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e.Message.Contains(ActivePerDayIndexName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Kimlikle kayıt: aktif klinik bağlamı dışındaki ya da kullanıcıya atanmamış klinikteki kayıt sızdırılmaz.</summary>
    internal static async Task<Result<CashCount>> FindInScopeAsync(
        IRepository<CashCount> counts,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        Guid tenantId,
        Guid id,
        CancellationToken ct)
    {
        var count = await counts.FirstOrDefaultAsync(new CashCountForUpdateSpec(tenantId, id), ct);
        if (count is null || (clinicContext.ClinicId is { } contextClinicId && count.ClinicId != contextClinicId))
            return Result<CashCount>.Failure("CashCounts.NotFound", "Kasa sayımı bulunamadı.");

        var access = await scopeResolver.ResolveAsync(tenantId, count.ClinicId, ct);
        return access.IsSuccess ? Result<CashCount>.Success(count) : Result<CashCount>.Failure(access.Error);
    }
}
