using Backend.Veteriner.Application.Payments.Specs;

namespace Backend.Veteriner.Application.Dashboard;

/// <summary>
/// Dashboard finans özetinde <see cref="PaymentPaidAtAmountInWindowSpec"/> ile yüklenen satırlar üzerinden
/// gün / hafta / ay toplamlarını hesaplar. Önceki tek-geçiş sadece "ay içi" tarama yaklaşımı,
/// hafta cuma–pazar veya ay değişiminde eski ayın günlerindeki ödemeleri kaçırdığı için burada
/// ayrılmıştır (union pencere tek DB sorgusu — bkz. <c>GetDashboardFinanceSummaryQueryHandler</c>).
/// </summary>
internal static class DashboardFinanceWindowAggregation
{
    /// <summary>
    /// [start, end) yarı-açık aralıkları ile bugün / ISO hafta / İstanbul takvim ayı kovalarına böler.
    /// Bir ödeme birden fazla kovaya düşebilir (bugün ⊆ hafta ⊆ ay değil; bugün ⊆ hafta ve ay ayrı sayılır).
    /// </summary>
    public static void SumBuckets(
        IReadOnlyList<PaymentPaidAtAmountRow> rows,
        DateTime dayStart,
        DateTime dayEnd,
        DateTime weekStart,
        DateTime weekEnd,
        DateTime monthStart,
        DateTime monthEnd,
        out decimal todayTotalPaid,
        out int todayPaymentsCount,
        out decimal weekTotalPaid,
        out int weekPaymentsCount,
        out decimal monthTotalPaid,
        out int monthPaymentsCount)
    {
        todayTotalPaid = 0m;
        todayPaymentsCount = 0;
        weekTotalPaid = 0m;
        weekPaymentsCount = 0;
        monthTotalPaid = 0m;
        monthPaymentsCount = 0;

        foreach (var p in rows)
        {
            var t = p.PaidAtUtc;
            if (t >= monthStart && t < monthEnd)
            {
                monthTotalPaid += p.Amount;
                monthPaymentsCount++;
            }

            if (t >= weekStart && t < weekEnd)
            {
                weekTotalPaid += p.Amount;
                weekPaymentsCount++;
            }

            if (t >= dayStart && t < dayEnd)
            {
                todayTotalPaid += p.Amount;
                todayPaymentsCount++;
            }
        }
    }
}
