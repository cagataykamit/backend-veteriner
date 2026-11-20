using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.CashRegisters;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Reports.DailyTransactions.Queries;
using Backend.Veteriner.Application.Reports.OutstandingBalances;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Hub;

/// <summary>
/// Ana ekranın para bölümleri. Hesap kopyalanmaz: ciro gün sonu dökümünün toplamı, tahsilat kasa özeti, alacak borç raporu, kasa bakiyesi
/// kasa kuralıdır (performans özetiyle aynı kaynaklar). İzin yoksa bölüm null ve sorgu çalışmaz.
/// </summary>
public sealed class ReportHubFinanceLoader
{
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IDailyTransactionsReader _transactions;
    private readonly ICashEventsReader _cashEvents;
    private readonly IOutstandingBalancesReportReader _outstanding;
    private readonly IReadRepository<CashRegister> _registers;
    private readonly IReadRepository<CashMovement> _movements;

    public ReportHubFinanceLoader(
        ICurrentUserPermissionChecker permissions,
        IDailyTransactionsReader transactions,
        ICashEventsReader cashEvents,
        IOutstandingBalancesReportReader outstanding,
        IReadRepository<CashRegister> registers,
        IReadRepository<CashMovement> movements)
    {
        _permissions = permissions;
        _transactions = transactions;
        _cashEvents = cashEvents;
        _outstanding = outstanding;
        _registers = registers;
        _movements = movements;
    }

    /// <summary>Bugünün kasası; <paramref name="today"/> bugün dönemidir (serisi son 7 gün).</summary>
    public async Task<Result<ReportHubTodayCashDto?>> LoadTodayCashAsync(Guid tenantId, Guid clinicId, ReportHubPeriod today, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Payments.Read))
            return Result<ReportHubTodayCashDto?>.Success(null);

        var days = today.Series;
        var window = new ReportHubWindow(days[0].FromUtc, today.Current.ToUtcExclusive, days[0].From, today.Current.To);
        var summary = await CashSummaryLoader.LoadAsync(_cashEvents, Filter(tenantId, clinicId, window), window.From, window.To, ct);
        if (!summary.IsSuccess)
            return Result<ReportHubTodayCashDto?>.Failure(summary.Error);

        var byDay = summary.Value!.ByDay.ToDictionary(d => d.Date);
        var last7Days = days.Select(d => new ReportHubSeriesPointDto(d.From, d.To, byDay.TryGetValue(d.From, out var x) ? x.NetAmount : 0m)).ToList();
        byDay.TryGetValue(today.Current.From, out var todayRow);

        decimal? mainBalance = null;
        if (_permissions.HasPermission(PermissionCatalog.CashRegisters.Read))
        {
            var main = await CashRegisterRules.ResolveAsync(_registers, tenantId, clinicId, null, ct);
            var totals = await CashRegisterRules.LoadTotalsAsync(
                _cashEvents, _movements, tenantId, clinicId, main.Value, null, today.Current.To, ct);
            mainBalance = totals.NetAmount;
        }

        return Result<ReportHubTodayCashDto?>.Success(
            new ReportHubTodayCashDto(todayRow?.NetAmount ?? 0m, todayRow?.CollectedCount ?? 0, last7Days, mainBalance));
    }

    /// <summary>Ciro = gün sonu dökümünün <c>NetChargeAmount</c>'u; gün sonu dökümü gibi tahsilat ve ücret okuma izni ister.</summary>
    public async Task<ReportHubTurnoverDto?> LoadTurnoverAsync(Guid tenantId, Guid clinicId, ReportHubPeriod period, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Payments.Read) || !_permissions.HasPermission(PermissionCatalog.Charges.Read))
            return null;

        var series = new List<ReportHubSeriesPointDto>(period.Series.Count);
        foreach (var w in period.Series)
            series.Add(new ReportHubSeriesPointDto(w.From, w.To, await NetChargeAsync(tenantId, clinicId, w, ct)));

        return new ReportHubTurnoverDto(
            await NetChargeAsync(tenantId, clinicId, period.Current, ct),
            await NetChargeAsync(tenantId, clinicId, period.Previous, ct),
            series);
    }

    public async Task<Result<ReportHubCollectionsDto?>> LoadCollectionsAsync(Guid tenantId, Guid clinicId, ReportHubPeriod period, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Payments.Read))
            return Result<ReportHubCollectionsDto?>.Success(null);

        var w = period.Current;
        var summary = await CashSummaryLoader.LoadAsync(_cashEvents, Filter(tenantId, clinicId, w), w.From, w.To, ct);
        if (!summary.IsSuccess)
            return Result<ReportHubCollectionsDto?>.Failure(summary.Error);

        var s = summary.Value!;
        return Result<ReportHubCollectionsDto?>.Success(new ReportHubCollectionsDto(
            s.Totals.NetAmount,
            s.Totals.CollectedCount,
            s.ByMethod.Select(m => new ReportHubMethodAmountDto(m.PaymentMethod, m.NetAmount)).ToList()));
    }

    /// <summary>Bugünkü açık alacak (borç raporunun toplamı); en yüksek borç raporun ilk satırıdır (tutara göre azalan).</summary>
    public async Task<ReportHubOutstandingDto?> LoadOutstandingAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        if (!_permissions.HasPermission(PermissionCatalog.Charges.Read))
            return null;

        var read = await _outstanding.ReadAsync(new OutstandingBalancesReadRequest(tenantId, clinicId, null, null, null, 0, 1), ct);
        return new ReportHubOutstandingDto(read.TotalOutstandingAmount, read.TotalCount, read.Items.FirstOrDefault()?.OutstandingAmount ?? 0m);
    }

    private async Task<decimal> NetChargeAsync(Guid tenantId, Guid clinicId, ReportHubWindow window, CancellationToken ct)
        => (await _transactions.ReadAsync(Filter(tenantId, clinicId, window), 0, 0, ct)).Totals.NetChargeAmount;

    private static FinanceEventFilter Filter(Guid tenantId, Guid clinicId, ReportHubWindow window)
        => new(tenantId, clinicId, null, window.FromUtc, window.ToUtcExclusive, null, null, null);
}
