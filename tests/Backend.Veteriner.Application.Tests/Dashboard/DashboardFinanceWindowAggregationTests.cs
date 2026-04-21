using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Application.Payments.Specs;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Dashboard;

/// <summary>
/// Ay değişiminde sadece "bu ay" tarayan eski dashboard akışının hafta toplamını yanlış sıfırlayıp sıfırlamadığını kilitler.
/// </summary>
public sealed class DashboardFinanceWindowAggregationTests
{
    [Fact]
    public void SumBuckets_Should_CountPaymentInIsoWeek_When_PaidAt_IsBeforeCurrentIstanbulMonth()
    {
        // 1 Nisan 2026 Çarşamba — İstanbul ayı 1'inde başlar; hafta Pazartesi (30 Mart) ile başlar.
        var utcNow = new DateTime(2026, 4, 1, 9, 0, 0, DateTimeKind.Utc);

        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(utcNow);
        var (weekStart, weekEnd) = OperationPeriodBounds.WeekForUtcNow(utcNow);
        var (monthStart, monthEnd) = OperationPeriodBounds.MonthForUtcNow(utcNow);

        weekStart.Should().BeBefore(monthStart);
        dayStart.Should().Be(monthStart);

        // 31 Mart gece geç saat — hâlâ aynı ISO haftası içinde, ancak Nisan ayı penceresi dışında.
        var paidInPrevMonthStillThisWeek = monthStart.AddMinutes(-90);
        paidInPrevMonthStillThisWeek.Should().BeBefore(monthStart);
        paidInPrevMonthStillThisWeek.Should().BeOnOrAfter(weekStart);
        paidInPrevMonthStillThisWeek.Should().BeBefore(weekEnd);

        var rows = new List<PaymentPaidAtAmountRow>
        {
            new(paidInPrevMonthStillThisWeek, 99m),
        };

        DashboardFinanceWindowAggregation.SumBuckets(
            rows,
            dayStart,
            dayEnd,
            weekStart,
            weekEnd,
            monthStart,
            monthEnd,
            out var today,
            out var todayCount,
            out var week,
            out var weekCount,
            out var month,
            out var monthCount);

        month.Should().Be(0m);
        monthCount.Should().Be(0);
        week.Should().Be(99m);
        weekCount.Should().Be(1);
        today.Should().Be(0m);
        todayCount.Should().Be(0);
    }
}
