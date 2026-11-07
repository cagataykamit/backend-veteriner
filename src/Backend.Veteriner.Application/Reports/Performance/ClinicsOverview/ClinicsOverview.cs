using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.OutstandingBalances;
using Backend.Veteriner.Application.Reports.Performance.Breakdowns;
using Backend.Veteriner.Application.Reports.Performance.Queries;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Performance.ClinicsOverview;

/// <summary>
/// Bir şubenin ya da toplamın dönem göstergeleri. Ciro = gün sonu <c>netChargeAmount</c>, tahsilat = kasa özeti <c>totals.netAmount</c>,
/// geliş = kırılımlardaki geliş sayısı; izni olmayan alan <c>null</c> (özetle aynı kural).
/// </summary>
public sealed record ClinicsOverviewFiguresDto(
    decimal? NetChargeAmount,
    decimal? NetCollectedAmount,
    int? VisitCount,
    int? ExaminationCount,
    AppointmentReportStatusCountsDto? Appointments);

/// <summary>Şube satırı; <c>Outstanding</c> bugünkü açık alacaktır, dönemden bağımsız.</summary>
public sealed record ClinicsOverviewRowDto(
    Guid ClinicId,
    string ClinicName,
    ClinicsOverviewFiguresDto Current,
    ClinicsOverviewFiguresDto? Previous,
    PerformanceOutstandingDto? Outstanding);

/// <summary>
/// Toplam: listelenen şubelerin birlikte okunması. Tutar ve sayılar satırların toplamına eşittir; alacaklı müşteri sayısı tekildir
/// (iki şubede borcu olan müşteri bir kez sayılır).
/// </summary>
public sealed record ClinicsOverviewLineDto(
    ClinicsOverviewFiguresDto Current,
    ClinicsOverviewFiguresDto? Previous,
    PerformanceOutstandingDto? Outstanding);

public sealed record ClinicsOverviewDto(
    string Currency,
    DateOnly From,
    DateOnly To,
    DateOnly? PreviousFrom,
    DateOnly? PreviousTo,
    IReadOnlyList<ClinicsOverviewRowDto> Clinics,
    ClinicsOverviewLineDto Total);

/// <summary>
/// Patron ekranı (TIER-007; docs/CLINICS_OVERVIEW_API_CONTRACT.md): kullanıcının erişebildiği tüm aktif şubeler, şube başına ve toplam,
/// önceki dönemle. Tarih kuralı işletme performansı özetiyle aynıdır; klinik filtresi yoktur.
/// </summary>
public sealed record GetClinicsOverviewQuery(DateOnly From, DateOnly To, bool Compare) : IRequest<Result<ClinicsOverviewDto>>;

/// <summary>
/// Şube satırı, o kliniğe açılan işletme performansı özetinin aynı yüklemesidir (<see cref="PerformancePeriodLoader"/>); geliş kırılım
/// okuyucusundan gelir. Paralel hesap yoktur.
/// </summary>
public sealed class GetClinicsOverviewQueryHandler : IRequestHandler<GetClinicsOverviewQuery, Result<ClinicsOverviewDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IPerformanceBreakdownReader _activity;
    private readonly PerformancePeriodLoader _periods;

    public GetClinicsOverviewQueryHandler(
        ITenantContext tenantContext,
        IClinicReadScopeResolver scopeResolver,
        ICurrentUserPermissionChecker permissions,
        TenantPlanFeatureEvaluator planFeatures,
        IReadRepository<Clinic> clinics,
        IPerformanceBreakdownReader activity,
        IDailyTransactionsReader transactions,
        ICashEventsReader cashEvents,
        IOutstandingBalancesReportReader outstanding,
        IReadRepository<Examination> examinations,
        IReadRepository<Vaccination> vaccinations,
        IAppointmentsReportStatusBreakdownReader appointments)
    {
        _tenantContext = tenantContext;
        _scopeResolver = scopeResolver;
        _permissions = permissions;
        _planFeatures = planFeatures;
        _clinics = clinics;
        _activity = activity;
        _periods = new PerformancePeriodLoader(permissions, transactions, cashEvents, outstanding, examinations, vaccinations, appointments);
    }

    public async ValueTask<Result<ClinicsOverviewDto>> Handle(GetClinicsOverviewQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ClinicsOverviewDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var planAllows = await _planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.ClinicsOverview, ct);
        if (!planAllows.IsSuccess)
            return Result<ClinicsOverviewDto>.Failure(planAllows.Error);

        if (RegisterReportFilter.RangeError(request.From, request.To) is { } rangeError)
            return Result<ClinicsOverviewDto>.Failure(rangeError);

        // Aktif klinik bağlamı bilerek kullanılmaz: ekranın işi tüm şubelerdir. Sınır raporlarla aynı çözümleyicidir
        // (Admin/Owner kiracı geneli, diğer roller atandığı klinikler).
        var scope = await _scopeResolver.ResolveAsync(tenantId, null, ct);
        if (!scope.IsSuccess)
            return Result<ClinicsOverviewDto>.Failure(scope.Error);
        var accessible = scope.Value!.AccessibleClinicIds;

        var clinics = (await _clinics.ListAsync(new ClinicsByTenantFilteredSpec(tenantId, isActive: true), ct))
            .Where(c => accessible is null || accessible.Contains(c.Id))
            .ToList();

        (DateOnly From, DateOnly To)? previous = request.Compare ? GetPerformanceSummaryQueryHandler.PreviousPeriod(request.From, request.To) : null;

        var rows = new List<ClinicsOverviewRowDto>(clinics.Count);
        foreach (var clinic in clinics)
        {
            var line = await LoadLineAsync(tenantId, clinic.Id, null, request, previous, ct);
            if (!line.IsSuccess)
                return Result<ClinicsOverviewDto>.Failure(line.Error);
            var l = line.Value!;
            rows.Add(new ClinicsOverviewRowDto(clinic.Id, clinic.Name, l.Current, l.Previous, l.Outstanding));
        }

        var total = await LoadLineAsync(tenantId, null, clinics.Select(c => c.Id).ToArray(), request, previous, ct);
        if (!total.IsSuccess)
            return Result<ClinicsOverviewDto>.Failure(total.Error);

        return Result<ClinicsOverviewDto>.Success(new ClinicsOverviewDto(
            "TRY", request.From, request.To, previous?.From, previous?.To, rows, total.Value!));
    }

    /// <summary>Tek şube (<paramref name="clinicId"/>) ya da listelenen şubeler birlikte (<paramref name="clinicIds"/>).</summary>
    private async Task<Result<ClinicsOverviewLineDto>> LoadLineAsync(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? clinicIds,
        GetClinicsOverviewQuery request,
        (DateOnly From, DateOnly To)? previous,
        CancellationToken ct)
    {
        var current = await LoadFiguresAsync(tenantId, clinicId, clinicIds, request.From, request.To, ct);
        if (!current.IsSuccess)
            return Result<ClinicsOverviewLineDto>.Failure(current.Error);

        ClinicsOverviewFiguresDto? before = null;
        if (previous is { } p)
        {
            var loaded = await LoadFiguresAsync(tenantId, clinicId, clinicIds, p.From, p.To, ct);
            if (!loaded.IsSuccess)
                return Result<ClinicsOverviewLineDto>.Failure(loaded.Error);
            before = loaded.Value;
        }

        var outstanding = _periods.CanSeeOutstanding ? await _periods.LoadOutstandingAsync(tenantId, clinicId, clinicIds, ct) : null;

        return Result<ClinicsOverviewLineDto>.Success(new ClinicsOverviewLineDto(current.Value!, before, outstanding));
    }

    private async Task<Result<ClinicsOverviewFiguresDto>> LoadFiguresAsync(
        Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? clinicIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var filter = DailyTransactionsReportFilter.ForScope(tenantId, clinicId, clinicIds, from, to);
        var period = await _periods.LoadAsync(filter, from, to, ct);
        if (!period.IsSuccess)
            return Result<ClinicsOverviewFiguresDto>.Failure(period.Error);
        var p = period.Value!;

        // Geliş, kırılımlardaki gibi geliş okuma izni ister.
        int? visitCount = _permissions.HasPermission(PermissionCatalog.Visits.Read) ? (await _activity.ReadActivityAsync(filter, ct)).VisitCount : null;

        return Result<ClinicsOverviewFiguresDto>.Success(new ClinicsOverviewFiguresDto(
            p.Charges?.NetChargeAmount, p.Cash?.Totals.NetAmount, visitCount, p.ExaminationCount, p.Appointments));
    }
}
