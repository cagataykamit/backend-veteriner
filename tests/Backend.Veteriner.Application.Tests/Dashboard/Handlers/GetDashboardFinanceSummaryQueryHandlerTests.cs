using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardFinanceSummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Payment>> _payments = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();

    private GetDashboardFinanceSummaryQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _payments.Object, _clients.Object, _pets.Object);

    private void SetupEmptyLookups()
    {
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentPaidAtAmountRow>());
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardFinancePaymentRow>());
        _clients
            .Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets
            .Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _payments.Verify(
            r => r.ListAsync(It.IsAny<ISpecification<Payment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnZeros_When_NoData()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyLookups();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var v = result.Value!;
        v.TodayTotalPaid.Should().Be(0m);
        v.WeekTotalPaid.Should().Be(0m);
        v.MonthTotalPaid.Should().Be(0m);
        v.TodayPaymentsCount.Should().Be(0);
        v.WeekPaymentsCount.Should().Be(0);
        v.MonthPaymentsCount.Should().Be(0);
        v.RecentPayments.Should().BeEmpty();
        v.Last7DaysPaid.Should().HaveCount(7);
        v.Last7DaysPaid.Should().OnlyContain(d => d.TotalAmount == 0m);
        v.Last7DaysPaid.Select(d => d.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Handle_Should_AggregateTotals_AndCounts_ForTodayWeekMonth()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var utcNow = DateTime.UtcNow;
        var (dayStart, _) = OperationDayBounds.ForUtcNow(utcNow);
        var (weekStart, _) = OperationPeriodBounds.WeekForUtcNow(utcNow);
        var (monthStart, _) = OperationPeriodBounds.MonthForUtcNow(utcNow);

        // Bu testin doğru çalışabilmesi için ay içinde en az bir gün gerek. Pratikte her zaman sağlanır.
        // Üç ayrı pencere için deterministik timestamp seçilir; boundary ilişkileri garanti olarak şu şekilde: dayStart >= weekStart >= monthStart.
        var inToday = dayStart.AddHours(1);
        var inWeekNotToday = weekStart < dayStart
            ? weekStart.AddHours(1)
            : dayStart.AddMinutes(30); // Hafta başı == gün başı ise (Pazartesi) bu ödeme today'e de düşer, toplamlara dahil edilir.
        var inMonthNotWeek = monthStart < weekStart
            ? monthStart.AddHours(1)
            : weekStart.AddMinutes(30); // Ay başı == hafta başı ise (ör. Pazartesi 1'i) bu ödeme week'e de düşer.

        var monthRows = new List<PaymentPaidAtAmountRow>
        {
            new(inToday, 150m),
            new(inWeekNotToday, 200m),
            new(inMonthNotWeek, 300m),
        };

        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(monthRows);
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardFinancePaymentRow>());
        _clients
            .Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var v = result.Value!;

        // Ay: tümü ayın içinde.
        v.MonthTotalPaid.Should().Be(650m);
        v.MonthPaymentsCount.Should().Be(3);

        // Hafta: inToday her zaman haftanın içindedir (dayStart >= weekStart).
        // inWeekNotToday tanımı gereği haftanın içindedir.
        // inMonthNotWeek: eğer monthStart >= weekStart ise (ör. 1'i Pazartesi), haftanın içindedir; yoksa dışındadır.
        var expectedWeekCount = monthStart >= weekStart ? 3 : 2;
        var expectedWeekTotal = monthStart >= weekStart ? 650m : 350m;
        v.WeekTotalPaid.Should().Be(expectedWeekTotal);
        v.WeekPaymentsCount.Should().Be(expectedWeekCount);

        // Bugün: inToday kesin içindedir. inWeekNotToday bazen (weekStart == dayStart) today'e düşer.
        // inMonthNotWeek pratikte today'e düşmez çünkü ya haftadan önce ya da en geç weekStart; boundary tam today ise zaten sayılır.
        var inWeekNotToday_InToday = inWeekNotToday >= dayStart;
        var inMonthNotWeek_InToday = inMonthNotWeek >= dayStart;
        var expectedTodayCount = 1 + (inWeekNotToday_InToday ? 1 : 0) + (inMonthNotWeek_InToday ? 1 : 0);
        var expectedTodayTotal = 150m
            + (inWeekNotToday_InToday ? 200m : 0m)
            + (inMonthNotWeek_InToday ? 300m : 0m);
        v.TodayTotalPaid.Should().Be(expectedTodayTotal);
        v.TodayPaymentsCount.Should().Be(expectedTodayCount);
    }

    [Fact]
    public async Task Handle_Should_ReadClinicContext_AndInvokeSpecs()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyLookups();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clinic.VerifyGet(c => c.ClinicId, Times.AtLeastOnce);
        // Ay/hafta/gün/trend tek birleşim penceresinde tek çağrı.
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Should_MapRecentPayments_WithClientAndPetNames()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentPaidAtAmountRow>());

        var client1 = new Client(tid, "Ayşe Kaya", "05321111111");
        var client2 = new Client(tid, "Mehmet Demir", "05322222222");
        var pet1 = new Pet(tid, client1.Id, "Pamuk", TestSpeciesIds.Cat);
        var pet2 = new Pet(tid, client2.Id, "Karabaş", TestSpeciesIds.Dog);

        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        var recentRows = new List<DashboardFinancePaymentRow>
        {
            new(p1, DateTime.UtcNow.AddMinutes(-1), client1.Id, cid, pet1.Id, 250m, "TRY", PaymentMethod.Cash),
            new(p2, DateTime.UtcNow.AddMinutes(-2), client2.Id, cid, pet2.Id, 400m, "TRY", PaymentMethod.Card),
            new(p3, DateTime.UtcNow.AddMinutes(-3), client1.Id, cid, null, 100m, "TRY", PaymentMethod.Transfer),
        };

        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recentRows);
        _clients
            .Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client> { client1, client2 });
        _pets
            .Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { pet1, pet2 });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var recent = result.Value!.RecentPayments;
        recent.Should().HaveCount(3);

        var first = recent[0];
        first.Id.Should().Be(p1);
        first.ClientId.Should().Be(client1.Id);
        first.ClientName.Should().Be("Ayşe Kaya");
        first.PetId.Should().Be(pet1.Id);
        first.PetName.Should().Be("Pamuk");
        first.Amount.Should().Be(250m);
        first.Currency.Should().Be("TRY");
        first.Method.Should().Be(PaymentMethod.Cash);

        var second = recent[1];
        second.ClientName.Should().Be("Mehmet Demir");
        second.PetName.Should().Be("Karabaş");

        var third = recent[2];
        third.PetId.Should().BeNull();
        third.PetName.Should().BeEmpty();
        third.ClientName.Should().Be("Ayşe Kaya");
    }

    [Fact]
    public async Task Handle_Should_FillLast7DaysPaid_WithZeros_WhenNoData()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupEmptyLookups();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var trend = result.Value!.Last7DaysPaid;
        trend.Should().HaveCount(7);
        trend.Should().OnlyContain(d => d.TotalAmount == 0m);
        trend.Select(d => d.Date).Should().BeInAscendingOrder();

        var istanbulToday = OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow)[^1].LocalDate;
        trend[^1].Date.Should().Be(istanbulToday);
        trend[0].Date.Should().Be(istanbulToday.AddDays(-6));
    }

    [Fact]
    public async Task Handle_Should_BucketPaymentsTrend_ByIstanbulDay()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var buckets = OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow);
        var todayBucket = buckets[6];
        var twoDaysAgoBucket = buckets[4];

        // Trend spesifik payment satırları: bugün 2 ödeme + 2 gün öncesi 1 ödeme.
        // Tek birleşim penceresi sorgusu bu satırları döndürür; trend bucket'ları ve ay toplamı aynı listeden gelir.
        var rows = new List<PaymentPaidAtAmountRow>
        {
            new(todayBucket.StartUtcInclusive.AddHours(3), 100m),
            new(todayBucket.StartUtcInclusive.AddHours(15), 250.50m),
            new(twoDaysAgoBucket.StartUtcInclusive.AddHours(10), 42m),
        };

        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardFinancePaymentRow>());
        _clients
            .Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var trend = result.Value!.Last7DaysPaid;
        trend.Should().HaveCount(7);
        trend[6].TotalAmount.Should().Be(350.50m);
        trend[4].TotalAmount.Should().Be(42m);
        trend.Where((_, i) => i != 4 && i != 6).Should().OnlyContain(d => d.TotalAmount == 0m);
        trend[6].Date.Should().Be(todayBucket.LocalDate);
        trend[4].Date.Should().Be(twoDaysAgoBucket.LocalDate);
    }

    [Fact]
    public async Task Handle_Should_CallTrendQuery_WithClinicScope_When_ClinicSelected()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);
        SetupEmptyLookups();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(
            r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        result.Value!.Last7DaysPaid.Should().HaveCount(7);
    }

    [Fact]
    public async Task Handle_Should_CountTodayPayment_CreatedLateEveningIstanbul_InTodayWindow()
    {
        // Regression: bugün toplamları 0 görünmesi — (a) create TZ §12.5, (b) eski handler’ın yalnızca “bu ay”
        // DB taramasından hafta/bugün süzmesi. Bu test (a) için: İstanbul gün sonu (23:30 yerel) ödemesinin
        // `todayTotalPaid` / `todayPaymentsCount` içinde sayıldığını doğrular.
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        var buckets = OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow);
        var todayBucket = buckets[6];
        // Günün son üç saati (bugün 21:00-23:59 İstanbul) UTC bazında bugün 18:00-20:59 civarı;
        // dashboard bugün penceresinin SON kısmındaki değeri seç.
        var lateTodayUtc = todayBucket.EndUtcExclusive.AddMinutes(-30); // today - 30dk
        lateTodayUtc.Should().BeOnOrAfter(todayBucket.StartUtcInclusive);
        lateTodayUtc.Should().BeBefore(todayBucket.EndUtcExclusive);

        var rows = new List<PaymentPaidAtAmountRow>
        {
            new(lateTodayUtc, 123.45m),
        };

        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardFinancePaymentRow>());
        _clients
            .Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var v = result.Value!;
        v.TodayTotalPaid.Should().Be(123.45m);
        v.TodayPaymentsCount.Should().Be(1);
        v.MonthTotalPaid.Should().Be(123.45m);
        v.MonthPaymentsCount.Should().Be(1);
        v.Last7DaysPaid[^1].TotalAmount.Should().Be(123.45m);
    }

    [Fact]
    public async Task Handle_Should_NotQueryPets_When_AllRecentPaymentsHaveNullPetId()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsPaidAtAmountInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PaymentPaidAtAmountRow>());

        var client = new Client(tid, "Zeynep Ak", "05320000000");
        var recentRows = new List<DashboardFinancePaymentRow>
        {
            new(Guid.NewGuid(), DateTime.UtcNow, client.Id, Guid.NewGuid(), null, 50m, "TRY", PaymentMethod.Cash),
        };

        _payments
            .Setup(r => r.ListAsync(It.IsAny<PaymentsForDashboardRecentSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recentRows);
        _clients
            .Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client> { client });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardFinanceSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.Value!.RecentPayments.Should().ContainSingle()
            .Which.PetName.Should().BeEmpty();
    }
}
