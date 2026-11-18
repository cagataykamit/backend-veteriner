using Ardalis.Specification;
using AwesomeAssertions;
using Backend.Veteriner.Application.Ai;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Tenants;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.Ai;

/// <summary>AI-007 kredi kuralı (ADR-044 madde 2, 5–7): kredi hesabı, kota/fon bölüşümü, oran ve ayda bir uyarı.</summary>
public sealed class AiCreditAccountTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly DateTime MonthStart = new(2026, 10, 31, 21, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MonthEnd = new(2026, 11, 30, 21, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAiUsageReader> _usage = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptions = new();
    private readonly Mock<IRepository<ClinicAiSettings>> _settingsRepo = new();
    private readonly Mock<IUserNotificationPublisher> _notifications = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
    private readonly ClinicAiSettings _settings = ClinicAiSettings.Create(TenantId, ClinicId, DateTime.UtcNow);

    public AiCreditAccountTests()
    {
        _subscriptions.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<TenantSubscription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSubscription.StartTrial(TenantId, SubscriptionPlanCode.Pro, DateTime.UtcNow, 14));
        _settingsRepo.Setup(s => s.FirstOrDefaultAsync(It.IsAny<ISpecification<ClinicAiSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_settings);
        SetTotals(AiClinicCreditTotals.Zero);
    }

    private AiCreditAccount Account(AiOptions? options = null)
        => new(Microsoft.Extensions.Options.Options.Create(options ?? new AiOptions()),
            _usage.Object, _subscriptions.Object, _settingsRepo.Object, _notifications.Object, _time);

    private void SetTotals(AiClinicCreditTotals totals)
        => _usage.Setup(u => u.ClinicCreditTotalsAsync(ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(totals);

    private static AiClinicQuota Quota(int monthly, decimal quotaUsed, decimal fundUsed = 0m, decimal fund = 0m)
        => new(monthly, quotaUsed, fundUsed, fund, 0, 0m, 20_000_000, 2m, MonthStart, MonthEnd);

    [Fact]
    public void Standard_Request_Is_One_Credit_On_Fast_Ai_And_Twenty_On_Top_Tier()
    {
        var options = new AiOptions();

        options.CreditsOf(options.PriceOf("claude-haiku-5-5")!.CostOf(2_000, 500)).Should().Be(1m);
        options.CreditsOf(options.PriceOf("claude-sonnet-5-5")!.CostOf(2_000, 500)).Should().Be(20m);
        options.CreditsOf(options.PriceOf("claude-haiku-5-5")!.CostOf(12_000, 2_000)).Should().Be(4.89m);
        options.CreditsOf(0m).Should().Be(0m);
    }

    [Theory]
    [InlineData("Basic", 0)]
    [InlineData("Pro", 500)]
    [InlineData("Premium", 2_000)]
    [InlineData("Unknown", 0)]
    [InlineData(null, 0)]
    public void Plan_Gift_Comes_From_The_Single_Setting(string? plan, int expected)
        => new AiOptions().MonthlyCreditsFor(plan).Should().Be(expected);

    [Fact]
    public void Credits_Are_Spent_From_Monthly_Quota_First_Then_From_Fund()
    {
        var quota = Quota(500, quotaUsed: 499m, fund: 10m);

        quota.Split(3m).Should().Be((1m, 2m));
        quota.After(1m, 2m, 100, 0.001m).Should().Match<AiClinicQuota>(q =>
            q.QuotaRemaining == 0m && q.FundBalance == 8m && q.MonthCreditsUsed == 502m && q.CreditsRemaining == 8m);
    }

    [Fact]
    public void Used_Percent_Is_Spent_Over_Spent_Plus_Remaining()
    {
        Quota(500, 400m).UsedPercent.Should().Be(80);
        Quota(500, 400m, fund: 400m).UsedPercent.Should().Be(44); // fon eklenince oran düşer
        Quota(500, 0m).UsedPercent.Should().Be(0);
        Quota(500, 500m).UsedPercent.Should().Be(100);
        Quota(0, 0m).IsExhausted.Should().BeTrue(); // hediyesiz paket, fon yok
    }

    [Fact]
    public void Warning_Level_Is_Eighty_Then_Hundred()
    {
        Quota(500, 399m).WarningLevel.Should().Be(0);
        Quota(500, 400m).WarningLevel.Should().Be(80);
        Quota(500, 500m).WarningLevel.Should().Be(100);
    }

    [Fact]
    public async Task Charge_Converts_Cost_To_Credits_And_Records_The_Fund_Part()
    {
        SetTotals(new AiClinicCreditTotals(0, 0m, 499.5m, 0m, FundGranted: 50m, FundUsed: 0m));

        var charge = await Account().ChargeAsync(TenantId, ClinicId, 2_500, 0.00045m * 2, CancellationToken.None);

        charge.Credits.Should().Be(2m);
        charge.FundCredits.Should().Be(1.5m);
    }

    [Fact]
    public async Task Zero_Cost_Request_Charges_Nothing_And_Reads_Nothing()
    {
        var charge = await Account().ChargeAsync(TenantId, ClinicId, 0, 0m, CancellationToken.None);

        charge.Should().Be(AiCreditCharge.None);
        _usage.Verify(u => u.ClinicCreditTotalsAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Crossing_Eighty_Percent_Warns_Clinic_Admins_Once_A_Month()
    {
        SetTotals(new AiClinicCreditTotals(0, 0m, 399m, 0m, 0m, 0m));
        var account = Account();

        await account.ChargeAsync(TenantId, ClinicId, 2_500, 0.00045m * 2, CancellationToken.None);
        SetTotals(new AiClinicCreditTotals(0, 0m, 401m, 0m, 0m, 0m));
        await account.ChargeAsync(TenantId, ClinicId, 2_500, 0.00045m * 2, CancellationToken.None);

        _notifications.Verify(n => n.AiQuotaWarningAsync(TenantId, ClinicId, false, MonthEnd, It.IsAny<CancellationToken>()), Times.Once);
        _settings.QuotaWarningLevel.Should().Be(80);
        _settings.QuotaWarningMonthStartUtc.Should().Be(MonthStart);
    }

    [Fact]
    public async Task Exhausting_Credits_Sends_The_End_Warning_Even_After_The_Eighty_Percent_One()
    {
        _settings.TryMarkQuotaWarning(MonthStart, 80);
        SetTotals(new AiClinicCreditTotals(0, 0m, 499m, 0m, 0m, 0m));

        await Account().ChargeAsync(TenantId, ClinicId, 2_500, 0.00045m * 3, CancellationToken.None);

        _notifications.Verify(n => n.AiQuotaWarningAsync(TenantId, ClinicId, true, MonthEnd, It.IsAny<CancellationToken>()), Times.Once);
        _settings.QuotaWarningLevel.Should().Be(100);
    }

    [Fact]
    public async Task Last_Months_Warning_Does_Not_Silence_This_Month()
    {
        _settings.TryMarkQuotaWarning(MonthStart.AddMonths(-1), 100);
        SetTotals(new AiClinicCreditTotals(0, 0m, 399m, 0m, 0m, 0m));

        await Account().ChargeAsync(TenantId, ClinicId, 2_500, 0.00045m * 2, CancellationToken.None);

        _notifications.Verify(n => n.AiQuotaWarningAsync(TenantId, ClinicId, false, MonthEnd, It.IsAny<CancellationToken>()), Times.Once);
    }
}
