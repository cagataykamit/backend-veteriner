using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Specs;
using Backend.Veteriner.Application.Reports.Examinations.Specs;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Hbs;
using Backend.Veteriner.Application.Reports.Vaccinations.Specs;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Reports.Hub;

/// <summary>
/// Ana ekranın iş hacmi bölümleri ve "Bakmanız gerekenler" bandı. Her sayı aynı veriyi gösteren raporun ya da sayfanın kuralıdır
/// (randevu, muayene, aşı raporlarının sayım spec'leri; HBS takip listeleri; gün sonu açık işleri; Özet'in aşı uyarıları; Stok durumu
/// düşük stok süzgeci). İzin yoksa alan null ve sorgu çalışmaz.
/// </summary>
public sealed class ReportHubWorkLoader
{
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IAppointmentsReportStatusBreakdownReader _appointmentStatuses;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<ProductStock> _stocks;
    private readonly IVisitOpenItemsReader _openItems;
    private readonly IRegisterReader<HbsPrescriptionRegisterItemDto> _hbsPrescriptions;
    private readonly IRegisterReader<HbsVaccinationRegisterItemDto> _hbsVaccinations;

    public ReportHubWorkLoader(
        ICurrentUserPermissionChecker permissions,
        IReadRepository<Appointment> appointments,
        IAppointmentsReportStatusBreakdownReader appointmentStatuses,
        IReadRepository<Examination> examinations,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Pet> pets,
        IReadRepository<ProductStock> stocks,
        IVisitOpenItemsReader openItems,
        IRegisterReader<HbsPrescriptionRegisterItemDto> hbsPrescriptions,
        IRegisterReader<HbsVaccinationRegisterItemDto> hbsVaccinations)
    {
        _permissions = permissions;
        _appointments = appointments;
        _appointmentStatuses = appointmentStatuses;
        _examinations = examinations;
        _vaccinations = vaccinations;
        _pets = pets;
        _stocks = stocks;
        _openItems = openItems;
        _hbsPrescriptions = hbsPrescriptions;
        _hbsVaccinations = hbsVaccinations;
    }

    /// <summary>Randevu raporunun durum dağılımı; toplam dört durumun toplamıdır (raporun <c>totalCount</c>'u). Seri tüm durumları sayar.</summary>
    public async Task<ReportHubAppointmentsDto?> LoadAppointmentsAsync(Guid tenantId, Guid clinicId, ReportHubPeriod period, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Appointments.Read))
            return null;

        var w = period.Current;
        // Randevu raporu kapalı aralık [from, to] ister; pencere sonunun hemen öncesi.
        var rows = await _appointmentStatuses.GetAsync(tenantId, clinicId, null, null, w.FromUtc, Inclusive(w), null, [], null, ct);
        var counts = AppointmentStatusCounts.From(rows);

        var series = new List<ReportHubSeriesPointDto>(period.Series.Count);
        foreach (var s in period.Series)
        {
            var count = await _appointments.CountAsync(
                new AppointmentsReportFilteredCountSpec(tenantId, clinicId, null, null, null, s.FromUtc, Inclusive(s), null, []), ct);
            series.Add(new ReportHubSeriesPointDto(s.From, s.To, count));
        }

        return new ReportHubAppointmentsDto(
            counts.Scheduled + counts.Completed + counts.Cancelled + counts.NoShow, counts.NoShow, counts, series);
    }

    public async Task<ReportHubExaminationsDto?> LoadExaminationsAsync(Guid tenantId, Guid clinicId, ReportHubPeriod period, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Examinations.Read))
            return null;

        var series = new List<ReportHubSeriesPointDto>(period.Series.Count);
        foreach (var s in period.Series)
            series.Add(new ReportHubSeriesPointDto(s.From, s.To, await CountExaminationsAsync(tenantId, clinicId, s, ct)));

        return new ReportHubExaminationsDto(await CountExaminationsAsync(tenantId, clinicId, period.Current, ct), series);
    }

    /// <summary>Uygulanan aşı aşı raporunun sayımı; yaklaşan aşı Özet'in "önümüzdeki 7 gün" uyarısıyla aynı (vefat eden hayvan hariç).</summary>
    public async Task<ReportHubVaccinationsDto?> LoadVaccinationsAsync(
        Guid tenantId, Guid clinicId, ReportHubPeriod period, DateTime nowUtc, IReadOnlyCollection<Guid>? petIdsNotReceivingCare, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Vaccinations.Read))
            return null;

        var w = period.Current;
        var applied = await _vaccinations.CountAsync(
            new VaccinationsReportFilteredCountSpec(tenantId, clinicId, null, null, VaccinationStatus.Applied, w.FromUtc, Inclusive(w), null, []), ct);
        var (fromUtc, toUtc) = VaccinationDueRule.UpcomingWindow(nowUtc, VaccinationDueRule.DashboardUpcomingDays);
        var upcoming = await _vaccinations.CountAsync(
            new DashboardUpcomingVaccinationsNext7DaysCountSpec(tenantId, clinicId, fromUtc, toUtc, null, petIdsNotReceivingCare), ct);

        return new ReportHubVaccinationsDto(applied, upcoming);
    }

    /// <summary>
    /// Bant. HBS sayıları Bugün'deki pencereyle (<see cref="OpenItemsSummaryRequest.ForToday"/>) ve HBS takip listesinin kuralıyla; en eski
    /// kayıt listenin ilk satırıdır (tarihe göre artan). Gün sonu açık işi gün sonu raporunun bugünkü "Ücret yok" sayısıdır.
    /// </summary>
    public async Task<ReportHubAttentionDto> LoadAttentionAsync(
        Guid tenantId, Guid clinicId, DateTime nowUtc, IReadOnlyCollection<Guid>? petIdsNotReceivingCare, CancellationToken ct)
    {
        var scope = OpenItemsScope.For(tenantId, clinicId, _permissions);
        var hbsWindow = OpenItemsSummaryRequest.ForToday(scope, nowUtc);
        var hbsFromUtc = hbsWindow.HbsFromUtc!.Value;
        var hbsToUtc = hbsWindow.HbsToUtc!.Value;

        ReportHubPendingDto? prescriptions = null;
        if (_permissions.HasPermission(PermissionCatalog.Prescriptions.Read))
        {
            var read = await _hbsPrescriptions.ReadAsync(HbsRequest(tenantId, clinicId, hbsFromUtc, hbsToUtc), ct);
            prescriptions = new ReportHubPendingDto(read.TotalCount, read.Items.FirstOrDefault()?.PrescribedAtUtc);
        }

        ReportHubPendingDto? vaccinations = null;
        int? overdueVaccinations = null;
        if (_permissions.HasPermission(PermissionCatalog.Vaccinations.Read))
        {
            var read = await _hbsVaccinations.ReadAsync(HbsRequest(tenantId, clinicId, hbsFromUtc, hbsToUtc), ct);
            vaccinations = new ReportHubPendingDto(read.TotalCount, read.Items.FirstOrDefault()?.AppliedAtUtc);
            overdueVaccinations = await _vaccinations.CountAsync(
                new DashboardOverdueVaccinationsCountSpec(
                    tenantId, clinicId, VaccinationDueRule.OverdueWindow(nowUtc), null, petIdsNotReceivingCare), ct);
        }

        int? uncharged = null;
        if (_permissions.HasPermission(PermissionCatalog.Visits.Read) && _permissions.HasPermission(PermissionCatalog.Charges.Read))
        {
            var (dayStartUtc, dayEndUtc) = OperationDayBounds.ForUtcNow(nowUtc);
            var summary = await _openItems.GetSummaryAsync(new OpenItemsSummaryRequest(scope, dayStartUtc, dayEndUtc, dayEndUtc), ct);
            uncharged = summary.UnchargedVisitCount;
        }

        int? lowStock = null;
        if (_permissions.HasPermission(PermissionCatalog.Products.Read))
        {
            lowStock = await _stocks.CountAsync(
                new ProductStocksFilteredCountSpec(
                    tenantId, clinicId, null, null, null, isBelowMinimum: true, null, null, OperationDayBounds.ToLocalDate(nowUtc)), ct);
        }

        return new ReportHubAttentionDto(prescriptions, vaccinations, uncharged, overdueVaccinations, lowStock);
    }

    /// <summary>Vefat eden hayvanlar (aşı uyarılarına girmez); yalnız aşı okuma izni varsa okunur.</summary>
    public async Task<IReadOnlyCollection<Guid>?> LoadPetIdsNotReceivingCareAsync(Guid tenantId, CancellationToken ct)
        => _permissions.HasPermission(PermissionCatalog.Vaccinations.Read)
            ? await _pets.ListAsync(new PetIdsNotReceivingCareSpec(tenantId), ct)
            : null;

    private Task<int> CountExaminationsAsync(Guid tenantId, Guid clinicId, ReportHubWindow w, CancellationToken ct)
        => _examinations.CountAsync(
            new ExaminationsReportFilteredCountSpec(tenantId, clinicId, null, null, null, w.FromUtc, w.ToUtcExclusive, null, [], null), ct);

    private static RegisterReadRequest HbsRequest(Guid tenantId, Guid clinicId, DateTime fromUtc, DateTime toUtc)
        => new(tenantId, clinicId, null, fromUtc, toUtc, false, null, null, 0, 1, HbsEntryStatus.NotEntered);

    private static DateTime Inclusive(ReportHubWindow w) => w.ToUtcExclusive.AddTicks(-1);
}
