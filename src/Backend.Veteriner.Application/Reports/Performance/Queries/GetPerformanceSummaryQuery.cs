using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.Examinations.Specs;
using Backend.Veteriner.Application.Reports.OutstandingBalances;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
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

/// <summary>
/// Hesap kopyalanmaz: ciro gün sonu dökümünün toplamı, tahsilat kasa özeti, alacak borç raporu, muayene/aşı sayısı raporların
/// sayım spec'leri, randevu dağılımı randevu raporunun okuyucusudur. Bölüm, raporun istediği izin yoksa <c>null</c> kalır.
/// </summary>
public sealed class GetPerformanceSummaryQueryHandler : IRequestHandler<GetPerformanceSummaryQuery, Result<PerformanceSummaryDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _scopeResolver;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IDailyTransactionsReader _transactions;
    private readonly CashSummaryLoader _cash;
    private readonly IOutstandingBalancesReportReader _outstanding;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IAppointmentsReportStatusBreakdownReader _appointments;

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
        _permissions = permissions;
        _transactions = transactions;
        _cash = new CashSummaryLoader(tenantContext, clinicContext, scopeResolver, cashEvents);
        _outstanding = outstanding;
        _examinations = examinations;
        _vaccinations = vaccinations;
        _appointments = appointments;
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
        if (_permissions.HasPermission(PermissionCatalog.Charges.Read))
        {
            var filter = await OutstandingBalancesReportFilter.ResolveAsync(
                _tenantContext, _clinicContext, _scopeResolver, request.ClinicId, null, null, ct);
            if (!filter.IsSuccess)
                return Result<PerformanceSummaryDto>.Failure(filter.Error);
            var read = await _outstanding.ReadAsync(filter.Value!.ToReadRequest(0, 0), ct);
            outstanding = new PerformanceOutstandingDto(read.TotalOutstandingAmount, read.TotalCount);
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
        var filter = resolved.Value!;

        // Aşı ve randevu sayımları kapalı aralık [from, to] ister; gün sonunun hemen öncesi.
        var toUtcInclusive = filter.ToUtcExclusive.AddTicks(-1);

        DailyTransactionTotalsDto? charges = null;
        if (_permissions.HasPermission(PermissionCatalog.Payments.Read) && _permissions.HasPermission(PermissionCatalog.Charges.Read))
            charges = (await _transactions.ReadAsync(filter, 0, 0, ct)).Totals;

        PerformanceCashDto? cash = null;
        if (_permissions.HasPermission(PermissionCatalog.Payments.Read))
        {
            var summary = await _cash.LoadAsync(from, to, clinicId, ct);
            if (!summary.IsSuccess)
                return Result<PerformancePeriodDto>.Failure(summary.Error);
            cash = PerformanceCashDto.From(summary.Value!);
        }

        int? examinationCount = null;
        if (_permissions.HasPermission(PermissionCatalog.Examinations.Read))
        {
            examinationCount = await _examinations.CountAsync(
                new ExaminationsReportFilteredCountSpec(
                    filter.TenantId, filter.ClinicId, null, null, null, filter.FromUtc, filter.ToUtcExclusive, null, [], filter.AccessibleClinicIds),
                ct);
        }

        PerformanceVaccinationCountsDto? vaccinations = null;
        if (_permissions.HasPermission(PermissionCatalog.Vaccinations.Read))
        {
            vaccinations = new PerformanceVaccinationCountsDto(
                await CountVaccinationsAsync(filter, VaccinationStatus.Applied, toUtcInclusive, ct),
                await CountVaccinationsAsync(filter, VaccinationStatus.Scheduled, toUtcInclusive, ct));
        }

        AppointmentReportStatusCountsDto? appointments = null;
        if (_permissions.HasPermission(PermissionCatalog.Appointments.Read))
        {
            var rows = await _appointments.GetAsync(
                filter.TenantId, filter.ClinicId, null, null, filter.FromUtc, toUtcInclusive, null, [], filter.AccessibleClinicIds, ct);
            appointments = AppointmentStatusCounts.From(rows);
        }

        return Result<PerformancePeriodDto>.Success(
            new PerformancePeriodDto(from, to, charges, cash, examinationCount, vaccinations, appointments));
    }

    private Task<int> CountVaccinationsAsync(FinanceEventFilter filter, VaccinationStatus status, DateTime toUtcInclusive, CancellationToken ct)
        => _vaccinations.CountAsync(
            new VaccinationsReportFilteredCountSpec(
                filter.TenantId, filter.ClinicId, null, null, status, filter.FromUtc, toUtcInclusive, null, [], filter.AccessibleClinicIds),
            ct);
}
