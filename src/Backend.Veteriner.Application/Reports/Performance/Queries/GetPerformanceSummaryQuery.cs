using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.OutstandingBalances;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Reports.Performance.Queries;

/// <summary>
/// İşletme performansı özeti (REPORT-008 dilim 1). <c>From</c>/<c>To</c>: İstanbul takvim günleri, ikisi dahil, en çok 366 gün
/// (gün sonu raporlarıyla aynı süzgeç). <c>Compare</c>: aynı uzunlukta bir önceki dönem de hesaplanır.
/// </summary>
public sealed record GetPerformanceSummaryQuery(DateOnly From, DateOnly To, Guid? ClinicId, bool Compare)
    : IRequest<Result<PerformanceSummaryDto>>;

/// <summary>Süzgeç gün sonu raporlarıyla aynı çözülür; dönem hesabı <see cref="PerformancePeriodLoader"/>'dadır.</summary>
public sealed class GetPerformanceSummaryQueryHandler : IRequestHandler<GetPerformanceSummaryQuery, Result<PerformanceSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly PerformancePeriodLoader _periods;

    public GetPerformanceSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        ICurrentUserPermissionChecker permissions,
        IDailyTransactionsReader transactions,
        ICashEventsReader cashEvents,
        IOutstandingBalancesReportReader outstanding,
        IReadRepository<Examination> examinations,
        IReadRepository<Vaccination> vaccinations,
        IAppointmentsReportStatusBreakdownReader appointments)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _scopeResolver = scopeResolver;
        _periods = new PerformancePeriodLoader(permissions, transactions, cashEvents, outstanding, examinations, vaccinations, appointments);
    }

    public async ValueTask<Result<PerformanceSummaryDto>> Handle(GetPerformanceSummaryQuery request, CancellationToken ct)
    {
        var current = await LoadPeriodAsync(request.From, request.To, request.ClinicId, ct);
        if (!current.IsSuccess)
            return Result<PerformanceSummaryDto>.Failure(current.Error);

        PerformancePeriodDto? previous = null;
        if (request.Compare)
        {
            var (from, to) = PreviousPeriod(request.From, request.To);
            var loaded = await LoadPeriodAsync(from, to, request.ClinicId, ct);
            if (!loaded.IsSuccess)
                return Result<PerformanceSummaryDto>.Failure(loaded.Error);
            previous = loaded.Value;
        }

        PerformanceOutstandingDto? outstanding = null;
        if (_periods.CanSeeOutstanding)
        {
            var filter = await OutstandingBalancesReportFilter.ResolveAsync(
                _tenantContext, _clinicContext, _scopeResolver, request.ClinicId, null, null, ct);
            if (!filter.IsSuccess)
                return Result<PerformanceSummaryDto>.Failure(filter.Error);
            var f = filter.Value!;
            outstanding = await _periods.LoadOutstandingAsync(f.TenantId, f.ClinicId, f.AccessibleClinicIds, ct);
        }

        return Result<PerformanceSummaryDto>.Success(new PerformanceSummaryDto("TRY", current.Value!, previous, outstanding));
    }

    /// <summary>Aynı uzunlukta, hemen önceki dönem (bu ay ↔ geçen ayın aynı gün sayısı; son 30 gün ↔ önceki 30 gün).</summary>
    public static (DateOnly From, DateOnly To) PreviousPeriod(DateOnly from, DateOnly to)
    {
        var length = to.DayNumber - from.DayNumber + 1;
        var previousTo = from.AddDays(-1);
        return (previousTo.AddDays(-(length - 1)), previousTo);
    }

    private async Task<Result<PerformancePeriodDto>> LoadPeriodAsync(DateOnly from, DateOnly to, Guid? clinicId, CancellationToken ct)
    {
        var resolved = await DailyTransactionsReportFilter.ResolveAsync(
            _tenantContext, _clinicContext, _scopeResolver, from, to, clinicId, null, null, null, ct);
        if (!resolved.IsSuccess)
            return Result<PerformancePeriodDto>.Failure(resolved.Error);

        return await _periods.LoadAsync(resolved.Value!, from, to, ct);
    }
}
