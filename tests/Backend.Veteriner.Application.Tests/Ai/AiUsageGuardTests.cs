using Ardalis.Specification;
using AwesomeAssertions;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Tenants;
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
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));

    public AiUsageGuardTests()
    {
        _generator.SetupGet(g => g.IsConfigured).Returns(true);
        SetClinicEnabled(true);
        SetPlan(SubscriptionPlanCode.Premium);
        SetClinicUsage(AiClinicCreditTotals.Zero);
        SetSystemUsage(AiUsageTotals.Zero);
        SetUserRequests(0);
    }

    private AiUsageGuard Guard(AiOptions? options = null)
    {
        var opts = Microsoft.Extensions.Options.Options.Create(options ?? new AiOptions());
        var credits = new AiCreditAccount(
            opts, _usage.Object, _subscriptions.Object, Mock.Of<IRepository<ClinicAiSettings>>(), Mock.Of<IUserNotificationPublisher>(), _time);
        return new(opts, _generator.Object, _settings.Object, _usage.Object, credits, _time);
    }

    private void SetClinicEnabled(bool enabled)
    {
        var row = ClinicAiSettings.Create(TenantId, ClinicId, DateTime.UtcNow);
        row.SetEnabled(enabled, null, DateTime.UtcNow);
        _settings.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<ClinicAiSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    private void SetPlan(SubscriptionPlanCode plan)
        => _subscriptions.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiCreditAccountTests.PaidSubscription(TenantId, plan));

    private void SetClinicUsage(AiClinicCreditTotals totals)
        => _usage.Setup(u => u.ClinicCreditTotalsAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task Used_Monthly_Credits_Without_Fund_Stop_Requests()
    {
        SetClinicUsage(new AiClinicCreditTotals(1_000, 0.9m, 2_000m, 0m, 0m, 0m));

        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.Error.Code.Should().Be("Ai.ClinicQuotaExceeded");
    }

    [Fact]
    public async Task Fund_Keeps_Ai_Running_After_Monthly_Credits_Are_Used()
    {
        SetClinicUsage(new AiClinicCreditTotals(1_000, 0.9m, 2_000m, 0m, FundGranted: 100m, FundUsed: 0m));

        var availability = await Guard().GetAvailabilityAsync(TenantId, ClinicId, CancellationToken.None);

        availability.Blocker.Should().BeNull();
        availability.Quota.CreditsRemaining.Should().Be(100m);
        availability.ClinicMonthUsedPercent.Should().Be(95);
    }

    [Fact]
    public async Task Basic_Plan_Has_No_Monthly_Gift_So_Ai_Needs_Fund()
    {
        SetPlan(SubscriptionPlanCode.Basic);

        var result = await Guard().CheckAsync(TenantId, ClinicId, UserId, CancellationToken.None);

        result.Error.Code.Should().Be("Ai.ClinicQuotaExceeded");
    }

    [Theory]
    [InlineData(20_000_000L, 0.5)]
    [InlineData(10L, 2.0)]
    public async Task Token_And_Cost_Insurance_Limits_Still_Stop_Requests(long tokens, double cost)
    {
        SetClinicUsage(new AiClinicCreditTotals(tokens, (decimal)cost, 10m, 0m, 0m, 0m));

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
        SetClinicUsage(new AiClinicCreditTotals(1_000_000, 1.7m, 500m, 0m, 0m, 0m)); // kredi %25, maliyet %85
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
        _usage.Setup(u => u.ClinicCreditTotalsAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, DateTime, CancellationToken>((_, f, _, _) => from = f)
            .ReturnsAsync(AiClinicCreditTotals.Zero);

        var availability = await Guard().GetAvailabilityAsync(TenantId, ClinicId, CancellationToken.None);

        from.Should().Be(new DateTime(2026, 11, 30, 21, 0, 0, DateTimeKind.Utc));
        availability.Quota.ResetsAtUtc.Should().Be(new DateTime(2026, 12, 31, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Cost_Uses_Per_Million_Prices()
        => new AiModelPrice { InputPerMillionUsd = 0.10m, OutputPerMillionUsd = 0.50m }
            .CostOf(2_000, 500).Should().Be(0.00045m);
}
