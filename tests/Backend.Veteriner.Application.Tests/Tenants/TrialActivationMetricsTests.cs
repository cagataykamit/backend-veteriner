using Backend.Veteriner.Application.Tenants.Activation;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants.Queries.GetTrialActivationMetrics;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants;

/// <summary>TRIAL-008 hesap kuralları (sözleşme §3): oran/null, 10 dk sınırı (eşitlik dahil), medyan, dağılım sırası, kohort karşılaştırması; sorgu dönem doğrulaması.</summary>
public sealed class TrialActivationMetricsTests
{
    private static readonly DateTime From = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    private static TrialCohortRow Row(
        double? firstActionAfterMinutes = null,
        ActivationSignalKind? kind = null,
        bool example = false,
        bool paid = false,
        int startDay = 5)
    {
        var start = From.AddDays(startDay);
        return new TrialCohortRow(
            Guid.NewGuid(),
            start,
            paid ? start.AddDays(14) : null,
            example ? start.AddSeconds(3) : null,
            firstActionAfterMinutes.HasValue ? start.AddMinutes(firstActionAfterMinutes.Value) : null,
            firstActionAfterMinutes.HasValue ? kind ?? ActivationSignalKind.ClientCreated : null);
    }

    [Fact]
    public void Calculate_Should_Return_Nulls_For_Empty_Cohort()
    {
        var dto = TrialActivationMetricsCalculator.Calculate(From, To, []);

        dto.TrialStarts.Should().Be(0);
        dto.ExampleClinic.Rate.Should().BeNull();
        dto.Activation.Rate.Should().BeNull();
        dto.Activation.MedianMinutesToFirstAction.Should().BeNull();
        dto.Conversion.Rate.Should().BeNull();
        dto.FirstActionBreakdown.Should().BeEmpty();
        dto.ByOnboarding.Select(b => b.Onboarding).Should().Equal("ExampleClinic", "EmptyClinic");
        dto.ByOnboarding.Should().OnlyContain(b => b.TrialStarts == 0 && b.ActivationRate == null);
    }

    [Fact]
    public void Calculate_Should_Compute_Rates_Quick_Window_Median_And_Breakdown()
    {
        var cohort = new List<TrialCohortRow>
        {
            Row(firstActionAfterMinutes: 4, kind: ActivationSignalKind.ClientCreated, example: true, paid: true),
            Row(firstActionAfterMinutes: 10, kind: ActivationSignalKind.AppointmentCreated, example: true), // eşitlik dahil
            Row(firstActionAfterMinutes: 10.5, kind: ActivationSignalKind.ClientCreated, example: false),
            Row(firstActionAfterMinutes: 60, kind: ActivationSignalKind.ExaminationCreated, example: true),
            Row(example: false),
        };

        var dto = TrialActivationMetricsCalculator.Calculate(From, To, cohort);

        dto.TrialStarts.Should().Be(5);
        dto.ExampleClinic.Should().Be(new TrialMetricRatioDto(3, 0.6m));
        dto.Activation.Count.Should().Be(4);
        dto.Activation.Rate.Should().Be(0.8m);
        dto.Activation.Within10MinutesCount.Should().Be(2);
        dto.Activation.Within10MinutesRate.Should().Be(0.4m);
        dto.Activation.MedianMinutesToFirstAction.Should().Be(10.3m, "çift eleman: (10 + 10,5) / 2 = 10,25 → 10,3");
        dto.Conversion.Should().Be(new TrialMetricRatioDto(1, 0.2m));

        dto.FirstActionBreakdown.Select(b => (b.Kind, b.Count)).Should().Equal(
            ("ClientCreated", 2), ("AppointmentCreated", 1), ("ExaminationCreated", 1));

        var example = dto.ByOnboarding.Single(b => b.Onboarding == "ExampleClinic");
        example.TrialStarts.Should().Be(3);
        example.ActivationRate.Should().Be(1m);
        example.Within10MinutesRate.Should().Be(0.6667m);
        example.ConversionRate.Should().Be(0.3333m);

        var empty = dto.ByOnboarding.Single(b => b.Onboarding == "EmptyClinic");
        empty.TrialStarts.Should().Be(2);
        empty.ActivationRate.Should().Be(0.5m);
        empty.Within10MinutesRate.Should().Be(0m);
        empty.ConversionRate.Should().Be(0m);
    }

    [Fact]
    public void Calculate_Should_Use_Middle_Element_For_Odd_Count()
    {
        var cohort = new List<TrialCohortRow> { Row(1), Row(7), Row(300) };

        TrialActivationMetricsCalculator.Calculate(From, To, cohort).Activation.MedianMinutesToFirstAction.Should().Be(7m);
    }

    [Fact]
    public async Task Handler_Should_Apply_Defaults_And_Pass_Effective_Range()
    {
        var now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new Mock<TimeProvider>();
        clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(now));
        var reader = new Mock<ITrialActivationMetricsReader>();
        reader.Setup(r => r.ListCohortAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await new GetTrialActivationMetricsQueryHandler(reader.Object, clock.Object)
            .Handle(new GetTrialActivationMetricsQuery(null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ToUtc.Should().Be(now);
        result.Value.FromUtc.Should().Be(now.AddDays(-30));
        reader.Verify(r => r.ListCohortAsync(now.AddDays(-30), now, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(367)]
    public async Task Handler_Should_Reject_Invalid_Range(int days)
    {
        var reader = new Mock<ITrialActivationMetricsReader>(MockBehavior.Strict);

        var result = await new GetTrialActivationMetricsQueryHandler(reader.Object, TimeProvider.System)
            .Handle(new GetTrialActivationMetricsQuery(From, From.AddDays(days)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("TrialMetrics.InvalidRange");
    }
}
