using Backend.Veteriner.Application.Reports.Hub;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Reports.Hub;

/// <summary>Rapor Merkezi dönem pencereleri (REPORT_HUB_SUMMARY_API_CONTRACT.md §2). İstanbul = UTC+3.</summary>
public sealed class ReportHubPeriodTests
{
    // Cuma 2026-11-20 12:30 İstanbul.
    private static readonly DateTime FridayNoonUtc = new(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Today_Should_Compare_With_Yesterday_Until_The_Same_Hour_And_Draw_Last_7_Days()
    {
        var p = ReportHubPeriod.For(ReportHubPeriod.Today, FridayNoonUtc);

        p.Current.Should().Be(new ReportHubWindow(Utc(2026, 11, 19, 21), FridayNoonUtc, D(2026, 11, 20), D(2026, 11, 20)));
        p.Previous.Should().Be(new ReportHubWindow(Utc(2026, 11, 18, 21), FridayNoonUtc.AddDays(-1), D(2026, 11, 19), D(2026, 11, 19)));
        p.Series.Select(s => s.From).Should().Equal(Enumerable.Range(14, 7).Select(d => D(2026, 11, d)));
        p.Series[^1].ToUtcExclusive.Should().Be(FridayNoonUtc);
        p.Series[0].ToUtcExclusive.Should().Be(Utc(2026, 11, 14, 21));
    }

    [Fact]
    public void Week_Should_Start_On_Monday_And_Compare_With_The_Same_Moment_Last_Week()
    {
        var p = ReportHubPeriod.For(ReportHubPeriod.Week, FridayNoonUtc);

        p.Current.From.Should().Be(D(2026, 11, 16));
        p.Previous.FromUtc.Should().Be(Utc(2026, 11, 8, 21));
        p.Previous.ToUtcExclusive.Should().Be(FridayNoonUtc.AddDays(-7));
        p.Previous.To.Should().Be(D(2026, 11, 13));
        p.Series.Select(s => s.From).Should().Equal(Enumerable.Range(16, 5).Select(d => D(2026, 11, d)));
    }

    [Fact]
    public void Week_On_Sunday_Should_Have_Seven_Points()
    {
        var sunday = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);

        var p = ReportHubPeriod.For(ReportHubPeriod.Week, sunday);

        p.Current.From.Should().Be(D(2026, 11, 16));
        p.Series.Should().HaveCount(7);
    }

    [Fact]
    public void Month_Should_Draw_Weeks_Clipped_To_The_Month_And_Now()
    {
        var p = ReportHubPeriod.For(ReportHubPeriod.Month, FridayNoonUtc);

        p.Current.From.Should().Be(D(2026, 11, 1));
        p.Previous.From.Should().Be(D(2026, 10, 1));
        p.Previous.To.Should().Be(D(2026, 10, 20));
        // 1 Kasım Pazar: ilk hafta tek gün, sonra Pazartesi başlayan haftalar, son hafta şimdiye kadar.
        p.Series.Select(s => (s.From, s.To)).Should().Equal(
            (D(2026, 11, 1), D(2026, 11, 1)),
            (D(2026, 11, 2), D(2026, 11, 8)),
            (D(2026, 11, 9), D(2026, 11, 15)),
            (D(2026, 11, 16), D(2026, 11, 20)));
        p.Series[^1].ToUtcExclusive.Should().Be(FridayNoonUtc);
    }

    [Fact]
    public void Month_Comparison_Should_Stop_At_The_End_Of_A_Shorter_Previous_Month()
    {
        var march31 = new DateTime(2026, 3, 31, 9, 0, 0, DateTimeKind.Utc);

        var p = ReportHubPeriod.For(ReportHubPeriod.Month, march31);

        p.Previous.From.Should().Be(D(2026, 2, 1));
        p.Previous.ToUtcExclusive.Should().Be(p.Current.FromUtc, "geçen ay daha kısa: ay sonunda kesilir");
        p.Previous.To.Should().Be(D(2026, 2, 28));
    }

    [Fact]
    public void At_Local_Midnight_Today_Should_Be_An_Empty_Window_On_Today()
    {
        var midnight = Utc(2026, 11, 19, 21);

        var p = ReportHubPeriod.For(ReportHubPeriod.Today, midnight);

        p.Current.Should().Be(new ReportHubWindow(midnight, midnight, D(2026, 11, 20), D(2026, 11, 20)));
        p.Previous.FromUtc.Should().Be(p.Previous.ToUtcExclusive);
        p.Series[^1].From.Should().Be(D(2026, 11, 20));
    }

    [Theory]
    [InlineData("today", true)]
    [InlineData("week", true)]
    [InlineData("month", true)]
    [InlineData("Week", false)]
    [InlineData("year", false)]
    [InlineData(null, false)]
    public void IsKnown_Should_Accept_Only_Contract_Names(string? name, bool expected)
        => ReportHubPeriod.IsKnown(name).Should().Be(expected);

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static DateTime Utc(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);
}
