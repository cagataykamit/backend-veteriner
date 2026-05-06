namespace Backend.Veteriner.Application.Dashboard;

/// <summary>
/// Ödeme tablosunda bugün / hafta / ay pencereleri için SUM ve COUNT (bellekte satır taşımadan).
/// Pencereler <c>[start, end)</c> yarı-açık — <see cref="DashboardFinanceWindowAggregation"/> ile uyumlu.
/// </summary>
public interface IDashboardFinancePaymentAggregatesReader
{
    Task<DashboardFinanceWindowTotals> GetTotalsAsync(
        Guid tenantId,
        Guid? clinicId,
        DateTime dayStartUtc,
        DateTime dayEndUtcExclusive,
        DateTime weekStartUtc,
        DateTime weekEndUtcExclusive,
        DateTime monthStartUtc,
        DateTime monthEndUtcExclusive,
        CancellationToken ct = default);
}
