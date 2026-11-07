using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.Performance.Queries;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Performance.Breakdowns;

/// <summary>Bir ücret grubunun dönemdeki net cirosu: oluşan ücret − dönemde iptal edilen ücret (gün sonu dökümüyle aynı kural).</summary>
public sealed record ChargeBreakdownRow(
    Guid? VeterinarianUserId,
    ChargeLineKind LineKind,
    Guid? SpeciesId,
    string Description,
    decimal NetAmount,
    int CreatedCount);

public sealed record ExaminationBreakdownRow(Guid? VeterinarianUserId, Guid? SpeciesId, int Count);

public sealed record PerformanceActivityDto(int VisitCount, int ClientCount);

/// <summary>
/// Kırılım okuyucusu (TIER-004). Ücret olayları gün sonu dökümünün mali olay kaynağından gelir; böylece kırılım toplamı
/// <c>netChargeAmount</c>'a eşittir. Hekim kuralı: ziyaretin sorumlu hekimi; ziyaretsiz muayenede kesinleştiren hekim; ikisi de yoksa atanmamış.
/// </summary>
public interface IPerformanceBreakdownReader
{
    Task<IReadOnlyList<ChargeBreakdownRow>> ReadChargesAsync(FinanceEventFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<ExaminationBreakdownRow>> ReadExaminationsAsync(FinanceEventFilter filter, CancellationToken ct = default);

    Task<PerformanceActivityDto> ReadActivityAsync(FinanceEventFilter filter, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> LoadUserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> LoadSpeciesNamesAsync(IReadOnlyCollection<Guid> speciesIds, CancellationToken ct = default);
}

/// <summary><see cref="VeterinarianUserId"/> boşsa "atanmamış" (muayenesiz satış, hekimsiz ziyaret).</summary>
public sealed record VeterinarianBreakdownDto(Guid? VeterinarianUserId, string Name, int? ExaminationCount, decimal? NetChargeAmount);

public sealed record LineKindBreakdownDto(string LineKind, decimal NetChargeAmount, int ChargeCount);

/// <summary>Kalem ücret açıklamasına göre gruplanır (ücret satırı ürün kimliği taşımaz).</summary>
public sealed record ChargeItemBreakdownDto(string Description, string LineKind, decimal NetChargeAmount, int ChargeCount);

public sealed record SpeciesBreakdownDto(Guid? SpeciesId, string Name, int? ExaminationCount, decimal? NetChargeAmount);

/// <summary>İzni olmayan bölüm <c>null</c>.</summary>
public sealed record PerformanceBreakdownPeriodDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<VeterinarianBreakdownDto>? ByVeterinarian,
    IReadOnlyList<LineKindBreakdownDto>? ByLineKind,
    IReadOnlyList<ChargeItemBreakdownDto>? TopItems,
    IReadOnlyList<SpeciesBreakdownDto>? BySpecies,
    PerformanceActivityDto? Activity);

public sealed record PerformanceBreakdownsDto(string Currency, PerformanceBreakdownPeriodDto Current, PerformanceBreakdownPeriodDto? Previous);

/// <summary>
/// Gelişmiş raporlar (TIER-004; docs/PERFORMANCE_BREAKDOWNS_API_CONTRACT.md): hekim, satır türü, kalem, tür kırılımı ve geliş etkinliği,
/// önceki dönemle. Süzgeç ve dönem kuralı işletme performansı özetiyle aynıdır.
/// </summary>
public sealed record GetPerformanceBreakdownsQuery(DateOnly From, DateOnly To, Guid? ClinicId, bool Compare)
    : IRequest<Result<PerformanceBreakdownsDto>>;

public sealed class GetPerformanceBreakdownsQueryHandler : IRequestHandler<GetPerformanceBreakdownsQuery, Result<PerformanceBreakdownsDto>>
{
    /// <summary>Kalem listesi uzunluğu; kalanı listeye girmez (toplam satır türü kırılımındadır).</summary>
    public const int TopItemCount = 10;

    public const string UnassignedName = "Atanmamış";
    public const string NoPetName = "Hayvan yok";

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IPerformanceBreakdownReader _reader;

    public GetPerformanceBreakdownsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICurrentUserPermissionChecker permissions,
        TenantPlanFeatureEvaluator planFeatures,
        IPerformanceBreakdownReader reader)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _permissions = permissions;
        _planFeatures = planFeatures;
        _reader = reader;
    }

    public async ValueTask<Result<PerformanceBreakdownsDto>> Handle(GetPerformanceBreakdownsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<PerformanceBreakdownsDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.AdvancedReports, ct);
        if (!planAllows.IsSuccess)
            return Result<PerformanceBreakdownsDto>.Failure(planAllows.Error);

        var current = await LoadPeriodAsync(request.From, request.To, request.ClinicId, ct);
        if (!current.IsSuccess)
            return Result<PerformanceBreakdownsDto>.Failure(current.Error);

        PerformanceBreakdownPeriodDto? previous = null;
        if (request.Compare)
        {
            var (from, to) = GetPerformanceSummaryQueryHandler.PreviousPeriod(request.From, request.To);
            var loaded = await LoadPeriodAsync(from, to, request.ClinicId, ct);
            if (!loaded.IsSuccess)
                return Result<PerformanceBreakdownsDto>.Failure(loaded.Error);
            previous = loaded.Value;
        }

        return Result<PerformanceBreakdownsDto>.Success(new PerformanceBreakdownsDto("TRY", current.Value!, previous));
    }

    private async Task<Result<PerformanceBreakdownPeriodDto>> LoadPeriodAsync(DateOnly from, DateOnly to, Guid? clinicId, CancellationToken ct)
    {
        var resolved = await DailyTransactionsReportFilter.ResolveAsync(
            _tenantContext, _clinicContext, _scopeResolver, from, to, clinicId, null, null, null, ct);
        if (!resolved.IsSuccess)
            return Result<PerformanceBreakdownPeriodDto>.Failure(resolved.Error);
        var filter = resolved.Value!;

        // İzinler özetle aynıdır; hekim kırılımı ayrıca kişi performansı izni ister (UX taslağı S1).
        var canSeeMoney = _permissions.HasPermission(PermissionCatalog.Payments.Read) && _permissions.HasPermission(PermissionCatalog.Charges.Read);
        var canSeeExaminations = _permissions.HasPermission(PermissionCatalog.Examinations.Read);
        var canSeePeople = _permissions.HasPermission(PermissionCatalog.Reports.Performance);

        var charges = canSeeMoney ? await _reader.ReadChargesAsync(filter, ct) : null;
        var examinations = canSeeExaminations ? await _reader.ReadExaminationsAsync(filter, ct) : null;

        IReadOnlyList<VeterinarianBreakdownDto>? byVeterinarian = null;
        if (canSeePeople && (charges is not null || examinations is not null))
        {
            var ids = (charges?.Select(c => c.VeterinarianUserId) ?? [])
                .Concat(examinations?.Select(e => e.VeterinarianUserId) ?? [])
                .Distinct()
                .ToList();
            var names = await _reader.LoadUserNamesAsync(ids.OfType<Guid>().ToArray(), ct);
            byVeterinarian = ids
                .Select(id => new VeterinarianBreakdownDto(
                    id,
                    id is { } v ? names.GetValueOrDefault(v, "Kullanıcı") : UnassignedName,
                    examinations?.Where(e => e.VeterinarianUserId == id).Sum(e => e.Count),
                    charges?.Where(c => c.VeterinarianUserId == id).Sum(c => c.NetAmount)))
                .OrderByDescending(r => r.NetChargeAmount ?? 0m)
                .ThenByDescending(r => r.ExaminationCount ?? 0)
                .ToList();
        }

        IReadOnlyList<SpeciesBreakdownDto>? bySpecies = null;
        if (charges is not null || examinations is not null)
        {
            var ids = (charges?.Select(c => c.SpeciesId) ?? [])
                .Concat(examinations?.Select(e => e.SpeciesId) ?? [])
                .Distinct()
                .ToList();
            var names = await _reader.LoadSpeciesNamesAsync(ids.OfType<Guid>().ToArray(), ct);
            bySpecies = ids
                .Select(id => new SpeciesBreakdownDto(
                    id,
                    id is { } s ? names.GetValueOrDefault(s, "Tür") : NoPetName,
                    examinations?.Where(e => e.SpeciesId == id).Sum(e => e.Count),
                    charges?.Where(c => c.SpeciesId == id).Sum(c => c.NetAmount)))
                .OrderByDescending(r => r.ExaminationCount ?? 0)
                .ThenByDescending(r => r.NetChargeAmount ?? 0m)
                .ToList();
        }

        var byLineKind = charges?
            .GroupBy(c => c.LineKind)
            .OrderBy(g => g.Key)
            .Select(g => new LineKindBreakdownDto(g.Key.ToString(), g.Sum(c => c.NetAmount), g.Sum(c => c.CreatedCount)))
            .ToList();

        var topItems = charges?
            .GroupBy(c => (c.Description, c.LineKind))
            .Select(g => new ChargeItemBreakdownDto(g.Key.Description, g.Key.LineKind.ToString(), g.Sum(c => c.NetAmount), g.Sum(c => c.CreatedCount)))
            .OrderByDescending(r => r.NetChargeAmount)
            .ThenBy(r => r.Description, StringComparer.Ordinal)
            .Take(TopItemCount)
            .ToList();

        var activity = _permissions.HasPermission(PermissionCatalog.Visits.Read) ? await _reader.ReadActivityAsync(filter, ct) : null;

        return Result<PerformanceBreakdownPeriodDto>.Success(
            new PerformanceBreakdownPeriodDto(from, to, byVeterinarian, byLineKind, topItems, bySpecies, activity));
    }
}
