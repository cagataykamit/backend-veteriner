using Ardalis.Specification;
using Backend.Veteriner.Application.CashCounts;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.Tenants;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.CashCounts;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.CashCounts;

/// <summary>
/// TIER-008 ilk dilimi: beklenen nakit kasa özetinin nakit satırından gelir (Kart/Havale sayılmaz, iptal ve kredi iadesi düşer), tek klinik
/// zorunlu, kaydetme Kurumsal ister, istemcinin gördüğü beklenen nakit eskiyse kayıt yazılmaz.
/// </summary>
public sealed class CashCountHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 11, 2);

    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IClinicReadScopeResolver> _scope = ClinicReadScopeResolverMock.Default();
    private readonly Mock<ICashEventsReader> _cashEvents = new();
    private readonly Mock<IReadRepository<CashCount>> _countsRead = new();
    private readonly Mock<IRepository<CashCount>> _counts = new();
    private readonly Mock<IReadRepository<User>> _users = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private FinanceEventFilter? _seenFilter;

    public CashCountHandlerTests()
    {
        _tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        _client.SetupGet(c => c.UserId).Returns(UserId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("Kasa", DateTime.UtcNow));
        _users.Setup(r => r.ListAsync(It.IsAny<ISpecification<User, CashCountUser>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CashCountUser(UserId, "Selin", "selin@example.com")]);
        _countsRead.Setup(r => r.ListAsync(It.IsAny<ISpecification<CashCount>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Nakit: 1000 tahsilat, 200 iptal, 50 kredi iadesi → net 750. Kart 400 sayılmaz.
        _cashEvents
            .Setup(r => r.ReadAsync(It.IsAny<FinanceEventFilter>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((FinanceEventFilter f, int _, CancellationToken _) => _seenFilter = f)
            .ReturnsAsync(
            [
                Event(false, 1_000m, PaymentMethod.Cash),
                Event(true, 200m, PaymentMethod.Cash),
                Event(false, 50m, PaymentMethod.Cash) with { IsCreditRefund = true },
                Event(false, 400m, PaymentMethod.Card),
            ]);
    }

    private static CashEventRow Event(bool voided, decimal amount, PaymentMethod method)
        => new(voided, new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc), Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "Müşteri",
            amount, method, false, UserId, "Selin", UserId, "Selin", null);

    private GetCashCountDayQueryHandler DayHandler()
        => new(_tenant.Object, _clinic.Object, _scope.Object, _cashEvents.Object, _countsRead.Object, _users.Object);

    private CreateCashCountCommandHandler CreateHandler(SubscriptionPlanCode plan)
        => new(_tenant.Object, _clinic.Object, _client.Object, _scope.Object, _tenants.Object, PlanFeatureTestFactory.ForPlan(plan),
            _cashEvents.Object, _counts.Object, _users.Object, TimeProvider.System);

    [Fact]
    public async Task Day_Should_Take_Expected_Cash_From_Cash_Summary_Cash_Row_For_One_Istanbul_Day()
    {
        var result = await DayHandler().Handle(new GetCashCountDayQuery(Day, ClinicId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        var day = result.Value!;
        day.Cash.CollectedAmount.Should().Be(1_000m);
        day.Cash.VoidedAmount.Should().Be(200m);
        day.Cash.RefundedCreditAmount.Should().Be(50m);
        day.Cash.NetAmount.Should().Be(750m);
        day.Current.Should().BeNull();
        day.ExpectedChangedSinceCount.Should().BeFalse();
        day.History.Should().BeEmpty();

        _seenFilter!.ClinicId.Should().Be(ClinicId);
        _seenFilter.FromUtc.Should().Be(new DateTime(2026, 11, 1, 21, 0, 0, DateTimeKind.Utc));
        _seenFilter.ToUtcExclusive.Should().Be(new DateTime(2026, 11, 2, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Day_Should_Flag_Cash_Movement_After_The_Count()
    {
        var count = CashCount.Create(TenantId, ClinicId, Day, Day, 100m, 700m, 800m, null, UserId, DateTime.UtcNow).Value!;
        _countsRead.Setup(r => r.ListAsync(It.IsAny<ISpecification<CashCount>>(), It.IsAny<CancellationToken>())).ReturnsAsync([count]);

        var day = (await DayHandler().Handle(new GetCashCountDayQuery(Day, ClinicId), CancellationToken.None)).Value!;

        day.Current!.Id.Should().Be(count.Id);
        day.Current.ExpectedCashAmount.Should().Be(700m, "sayım anındaki değer saklanır");
        day.Current.CreatedByName.Should().Be("Selin");
        day.ExpectedChangedSinceCount.Should().BeTrue();
    }

    [Fact]
    public async Task Day_Should_Require_A_Single_Clinic()
    {
        var result = await DayHandler().Handle(new GetCashCountDayQuery(Day, null), CancellationToken.None);

        result.Error.Code.Should().Be("CashCounts.ClinicScopeRequired");
        _cashEvents.Verify(r => r.ReadAsync(It.IsAny<FinanceEventFilter>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(SubscriptionPlanCode.Basic)]
    [InlineData(SubscriptionPlanCode.Pro)]
    public async Task Create_Should_Need_Premium(SubscriptionPlanCode plan)
    {
        var result = await CreateHandler(plan).Handle(
            new CreateCashCountCommand(ClinicId, Day, 100m, 850m, 750m, null), CancellationToken.None);

        result.Error.Code.Should().Be(TenantPlanFeatureEvaluator.FeatureNotInPlanCode);
        _counts.Verify(r => r.AddAsync(It.IsAny<CashCount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Refuse_When_The_Shown_Expected_Cash_Is_Stale()
    {
        var result = await CreateHandler(SubscriptionPlanCode.Premium).Handle(
            new CreateCashCountCommand(ClinicId, Day, 100m, 800m, 700m, null), CancellationToken.None);

        result.Error.Code.Should().Be("CashCounts.ExpectedChanged");
        _counts.Verify(r => r.AddAsync(It.IsAny<CashCount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Store_Server_Expected_Cash_And_Difference()
    {
        CashCount? added = null;
        _counts.Setup(r => r.AddAsync(It.IsAny<CashCount>(), It.IsAny<CancellationToken>()))
            .Callback((CashCount c, CancellationToken _) => added = c)
            .ReturnsAsync((CashCount c, CancellationToken _) => c);

        var result = await CreateHandler(SubscriptionPlanCode.Premium).Handle(
            new CreateCashCountCommand(ClinicId, Day, 100m, 845m, 750m, "5 TL para üstü eksik"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        added!.ExpectedCashAmount.Should().Be(750m);
        added.ClinicId.Should().Be(ClinicId);
        result.Value!.ExpectedClosingAmount.Should().Be(850m);
        result.Value.DifferenceAmount.Should().Be(-5m);
        result.Value.CreatedByName.Should().Be("Selin");
        _counts.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
