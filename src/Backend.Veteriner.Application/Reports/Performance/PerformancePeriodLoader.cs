using Backend.Veteriner.Application.Auth;
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

namespace Backend.Veteriner.Application.Reports.Performance;

/// <summary>
/// İşletme performansının bir dönemi ve açık alacağı, kapsamı çözülmüş süzgeçle. Hesap kopyalanmaz: ciro gün sonu dökümünün toplamı,
/// tahsilat kasa özeti, alacak borç raporu, muayene/aşı sayısı raporların sayım spec'leri, randevu dağılımı randevu raporunun okuyucusudur.
/// Özet (REPORT-008) ve patron ekranı (TIER-007) aynı yüklemeden geçer. Bölüm, raporun istediği izin yoksa <c>null</c> kalır.
/// </summary>
internal sealed class PerformancePeriodLoader
{
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IDailyTransactionsReader _transactions;
    private readonly ICashEventsReader _cashEvents;
    private readonly IOutstandingBalancesReportReader _outstanding;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IAppointmentsReportStatusBreakdownReader _appointments;

    public PerformancePeriodLoader(
        ICurrentUserPermissionChecker permissions,
        IDailyTransactionsReader transactions,
        ICashEventsReader cashEvents,
        IOutstandingBalancesReportReader outstanding,
        IReadRepository<Examination> examinations,
        IReadRepository<Vaccination> vaccinations,
        IAppointmentsReportStatusBreakdownReader appointments)
    {
        _permissions = permissions;
        _transactions = transactions;
        _cashEvents = cashEvents;
        _outstanding = outstanding;
        _examinations = examinations;
        _vaccinations = vaccinations;
        _appointments = appointments;
    }

    /// <summary>Açık alacak borç raporu gibi ücret okuma izni ister.</summary>
    public bool CanSeeOutstanding => _permissions.HasPermission(PermissionCatalog.Charges.Read);

    public async Task<Result<PerformancePeriodDto>> LoadAsync(FinanceEventFilter filter, DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Aşı ve randevu sayımları kapalı aralık [from, to] ister; gün sonunun hemen öncesi.
        var toUtcInclusive = filter.ToUtcExclusive.AddTicks(-1);

        DailyTransactionTotalsDto? charges = null;
        if (_permissions.HasPermission(PermissionCatalog.Payments.Read) && _permissions.HasPermission(PermissionCatalog.Charges.Read))
            charges = (await _transactions.ReadAsync(filter, 0, 0, ct)).Totals;

        PerformanceCashDto? cash = null;
        if (_permissions.HasPermission(PermissionCatalog.Payments.Read))
        {
            var summary = await CashSummaryLoader.LoadAsync(_cashEvents, filter, from, to, ct);
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

    /// <summary>Bugünkü açık alacak; çağıran önce <see cref="CanSeeOutstanding"/>'a bakar.</summary>
    public async Task<PerformanceOutstandingDto> LoadOutstandingAsync(
        Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, CancellationToken ct)
    {
        var read = await _outstanding.ReadAsync(new OutstandingBalancesReadRequest(tenantId, clinicId, accessibleClinicIds, null, null, 0, 0), ct);
        return new PerformanceOutstandingDto(read.TotalOutstandingAmount, read.TotalCount);
    }

    private Task<int> CountVaccinationsAsync(FinanceEventFilter filter, VaccinationStatus status, DateTime toUtcInclusive, CancellationToken ct)
        => _vaccinations.CountAsync(
            new VaccinationsReportFilteredCountSpec(
                filter.TenantId, filter.ClinicId, null, null, status, filter.FromUtc, toUtcInclusive, null, [], filter.AccessibleClinicIds),
            ct);
}
