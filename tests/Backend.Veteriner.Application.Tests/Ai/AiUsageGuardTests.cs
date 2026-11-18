using Ardalis.Specification;
using AwesomeAssertions;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.Ai;

public sealed class AiUsageGuardTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IAiTextGenerator> _generator = new();
    private readonly Mock<IReadRepository<ClinicAiSettings>> _settings = new();
    private readonly Mock<IAiUsageReader> _usage = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));

    public AiUsageGuardTests()
    {
        _generator.SetupGet(g => g.IsConfigured).Returns(true);
        SetClinicEnabled(true);
        SetClinicUsage(AiUsageTotals.Zero);
        SetSystemUsage(AiUsageTotals.Zero);
        SetUserRequests(0);
    }

    private AiUsageGuard Guard(AiOptions? options = null)
        => new(Microsoft.Extensions.Options.Options.Create(options ?? new AiOptions()), _generator.Object, _settings.Object, _usage.Object, _time);

    private void SetClinicEnabled(bool enabled)
    {
        var row = ClinicAiSettings.Create(TenantId, ClinicId, DateTime.UtcNow);
        row.SetEnabled(enabled, null, DateTime.UtcNow);
        _settings.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<ClinicAiSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    private void SetClinicUsage(AiUsageTotals totals)
        => _usage.Setup(u => u.ClinicTotalsAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

    private void SetSystemUsage(AiUsageTotals totals)
        => _usage.Setup(u => u.SystemTotalsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

    private void SetUserRequests(int count)
        => _usage.Setup(u => u.CountUserRequestsSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    [Fact]
    public async Task Grants_Model_Price_And_Token_Limits_When_Everything_Is_Within_Limits()
    {
        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Model.Should().Be("claude-haiku-5-5");
        result.Value.Price.InputPerMillionUsd.Should().Be(0.10m);
        result.Value.MaxInputTokens.Should().Be(12_000);
        result.Value.MaxOutputTokens.Should().Be(2_000);
    }

    [Fact]
    public async Task Not_Configured_Without_Provider_Key()
    {
        _generator.SetupGet(g => g.IsConfigured).Returns(false);

        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.Error.Code.Should().Be("Ai.NotConfigured");
    }

    [Fact]
    public async Task Not_Configured_When_Switched_Off_Or_Model_Has_No_Price()
    {
        (await Guard(new AiOptions { Enabled = false }).CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None))
            .Error.Code.Should().Be("Ai.NotConfigured");
        (await Guard(new AiOptions { Model = "claude-unknown" }).CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None))
            .Error.Code.Should().Be("Ai.NotConfigured");
    }

    [Fact]
    public async Task Clinic_Without_Settings_Row_Is_Disabled()
    {
        _settings.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<ClinicAiSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAiSettings?)null);

        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.Error.Code.Should().Be("Ai.ClinicNotEnabled");
    }

    [Theory]
    [InlineData(5_000_000L, 0.5)]
    [InlineData(10L, 2.0)]
    public async Task Clinic_Monthly_Token_Or_Cost_Limit_Stops_Requests(long tokens, double cost)
    {
        SetClinicUsage(new AiUsageTotals(10, tokens, (decimal)cost));

        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.Error.Code.Should().Be("Ai.ClinicQuotaExceeded");
    }

    [Fact]
    public async Task System_Monthly_Budget_Stops_Everyone()
    {
        SetSystemUsage(new AiUsageTotals(1000, 1, 10m));

        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.Error.Code.Should().Be("Ai.SystemBudgetExhausted");
    }

    [Fact]
    public async Task User_Per_Minute_Limit_Counts_The_Last_Sixty_Seconds()
    {
        DateTime? since = null;
        _usage.Setup(u => u.CountUserRequestsSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, CancellationToken>((_, s, _) => since = s)
            .ReturnsAsync(3);

        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.Error.Code.Should().Be("Ai.RateLimited");
        since.Should().Be(_time.GetUtcNow().UtcDateTime.AddMinutes(-1));
    }

    [Fact]
    public async Task Availability_Reports_Used_Percent_From_The_Larger_Ratio_Without_Rate_Limit()
    {
        SetClinicUsage(new AiUsageTotals(5, 1_000_000, 1.7m));
        SetUserRequests(99);

        var availability = await Guard().GetAvailabilityAsync(TenantId, ClinicId, CancellationToken.None);

        availability.Blocker.Should().BeNull();
        availability.ClinicMonthUsedPercent.Should().Be(85);
        availability.IsConfigured.Should().BeTrue();
        availability.IsClinicEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Clinic_Usage_Is_Read_For_The_Istanbul_Calendar_Month()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 30, 22, 0, 0, TimeSpan.Zero)); // İstanbul: 1 Aralık 01:00
        DateTime? from = null;
        _usage.Setup(u => u.ClinicTotalsAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, DateTime, CancellationToken>((_, f, _, _) => from = f)
            .ReturnsAsync(AiUsageTotals.Zero);

        await Guard().GetAvailabilityAsync(TenantId, ClinicId, CancellationToken.None);

        from.Should().Be(new DateTime(2026, 11, 30, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Cost_Uses_Per_Million_Prices()
        => new AiModelPrice { InputPerMillionUsd = 0.10m, OutputPerMillionUsd = 0.50m }
            .CostOf(2_000, 500).Should().Be(0.00045m);
}
