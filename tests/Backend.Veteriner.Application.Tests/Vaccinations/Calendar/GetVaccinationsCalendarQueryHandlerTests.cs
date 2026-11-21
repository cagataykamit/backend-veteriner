using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Vaccinations.Calendar;
using Backend.Veteriner.Application.Vaccinations.Queries.GetCalendar;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.Vaccinations.Calendar;

public sealed class GetVaccinationsCalendarQueryHandlerTests
{
    // İstanbul 2026-11-16 12:00; görüntülenen hafta 2026-11-16 (pazartesi) – 2026-11-23.
    private static readonly DateTime NowUtc = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FromUtc = new(2026, 11, 15, 21, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToUtc = FromUtc.AddDays(7);

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    public GetVaccinationsCalendarQueryHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(_clinicId);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetIdsNotReceivingCareSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(_petId, _clientId, "Pamuk") });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(_clientId, "Ali Veli") });
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<AppliedVaccinationDosesForPetsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaccinationDose>());
    }

    private GetVaccinationsCalendarQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _vaccinations.Object,
            _pets.Object,
            _clients.Object,
            new FakeTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)));

    private void Rows(params VaccinationCalendarRow[] rows)
        => _vaccinations.Setup(r => r.ListAsync(It.IsAny<VaccinationsCalendarSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(rows.ToList());

    private VaccinationCalendarRow Row(VaccinationStatus status, DateTime? appliedAtUtc, DateTime? dueAtUtc, string name = "Karma", Guid? definitionId = null, Guid? petId = null)
        => new(Guid.NewGuid(), _clinicId, petId ?? _petId, definitionId ?? Guid.NewGuid(), name, status, appliedAtUtc, dueAtUtc, null);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_WindowExceeds45Days()
    {
        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, FromUtc.AddDays(46)), CancellationToken.None);

        result.Error.Code.Should().Be("Vaccinations.Calendar.DateWindowTooLarge");
    }

    [Fact]
    public async Task Handle_Should_Require_ClinicScope()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc), CancellationToken.None);

        result.Error.Code.Should().Be("Vaccinations.ClinicScopeRequired");
        _vaccinations.Verify(r => r.ListAsync(It.IsAny<VaccinationsCalendarSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Not_Accessible()
    {
        var mock = ClinicReadScopeResolverMock.ForClinicAdmin([Guid.NewGuid()]);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var result = await new GetVaccinationsCalendarQueryHandler(
                _tenantContext.Object, _clinicContext.Object, mock.Object, _vaccinations.Object, _pets.Object, _clients.Object,
                new FakeTimeProvider(new DateTimeOffset(NowUtc, TimeSpan.Zero)))
            .Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc, Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_Map_Planned_NextDue_And_Applied_With_States()
    {
        var todayDue = NowUtc.AddHours(1);
        var nextWeekDue = NowUtc.AddDays(3);
        var appliedToday = NowUtc.AddHours(-1);
        Rows(
            Row(VaccinationStatus.Scheduled, null, todayDue, "Kuduz"),
            Row(VaccinationStatus.Applied, NowUtc.AddYears(-1), nextWeekDue, "Karma"),
            Row(VaccinationStatus.Applied, appliedToday, NowUtc.AddMonths(1), "İç parazit"));

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value!;
        items.Should().HaveCount(3);
        items.Select(i => (i.Kind, i.State, i.VaccineName)).Should().Equal(
            (VaccinationCalendarKind.Applied, CalendarItemState.Completed, "İç parazit"),
            (VaccinationCalendarKind.Planned, CalendarItemState.DueToday, "Kuduz"),
            (VaccinationCalendarKind.NextDue, CalendarItemState.Upcoming, "Karma"));
        items[0].Date.Should().Be(new DateOnly(2026, 11, 16));
        items[0].PetName.Should().Be("Pamuk");
        items[0].ClientName.Should().Be("Ali Veli");
        items[0].ClientId.Should().Be(_clientId);
    }

    [Fact]
    public async Task Handle_Should_Report_TimeSpecified_Only_For_Due_Rows_With_Real_Time()
    {
        var timedDue = NowUtc.AddHours(2);
        Rows(
            Row(VaccinationStatus.Scheduled, null, NowUtc.AddHours(1), "Kuduz"),
            Row(VaccinationStatus.Applied, NowUtc.AddYears(-1), timedDue, "Karma") with { DueTimeSpecified = true },
            Row(VaccinationStatus.Applied, NowUtc.AddHours(-1), NowUtc.AddMonths(1), "İç parazit") with { DueTimeSpecified = true });

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc), CancellationToken.None);

        result.Value!.Select(i => (i.Kind, i.VaccineName, i.TimeSpecified)).Should().Equal(
            (VaccinationCalendarKind.Applied, "İç parazit", false),
            (VaccinationCalendarKind.Planned, "Kuduz", false),
            (VaccinationCalendarKind.NextDue, "Karma", true));
        result.Value!.Single(i => i.TimeSpecified).AtUtc.Should().Be(timedDue);
    }

    [Fact]
    public async Task Handle_Should_Mark_Past_Due_In_Window_As_Overdue()
    {
        var lastWeekFrom = FromUtc.AddDays(-7);
        Rows(Row(VaccinationStatus.Applied, NowUtc.AddYears(-1), lastWeekFrom.AddDays(2)));

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(lastWeekFrom, FromUtc), CancellationToken.None);

        result.Value!.Single().State.Should().Be(CalendarItemState.Overdue);
    }

    // 2026-11-21 kararı: bugün İstanbul 2026-11-16 → pencere başı İstanbul 2026-10-16 00:00 (2026-10-15 21:00 UTC).
    [Fact]
    public async Task Handle_Should_Hide_Due_Rows_Older_Than_One_Calendar_Month()
    {
        var windowStartUtc = new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);
        var viewFrom = windowStartUtc.AddDays(-14);
        var oldApplied = windowStartUtc.AddDays(-3);
        Rows(
            Row(VaccinationStatus.Scheduled, null, windowStartUtc, "Kuduz"),
            Row(VaccinationStatus.Scheduled, null, windowStartUtc.AddMinutes(-1), "Karma"),
            Row(VaccinationStatus.Applied, NowUtc.AddYears(-1), windowStartUtc.AddDays(-1), "Lyme"),
            Row(VaccinationStatus.Applied, oldApplied, null, "İç parazit"));

        var all = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(viewFrom, viewFrom.AddDays(28)), CancellationToken.None);
        var upcomingOnly = await CreateHandler().Handle(
            new GetVaccinationsCalendarQuery(viewFrom, viewFrom.AddDays(28), States: [CalendarItemState.Upcoming]), CancellationToken.None);

        all.Value!.Select(i => (i.Kind, i.State, i.VaccineName)).Should().Equal(
            (VaccinationCalendarKind.Applied, CalendarItemState.Completed, "İç parazit"),
            (VaccinationCalendarKind.Planned, CalendarItemState.Overdue, "Kuduz"));
        upcomingOnly.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Hide_NextDue_Fulfilled_By_Later_Dose()
    {
        var definitionId = Guid.NewGuid();
        var source = Row(VaccinationStatus.Applied, NowUtc.AddYears(-1), NowUtc.AddDays(2), definitionId: definitionId);
        Rows(source);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<AppliedVaccinationDosesForPetsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VaccinationDose> { new(Guid.NewGuid(), _petId, definitionId, "Karma", NowUtc.AddDays(-10)) });

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc), CancellationToken.None);

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_Hide_Due_Of_Deceased_Pet_But_Keep_Applied()
    {
        var deceased = Guid.NewGuid();
        Rows(
            Row(VaccinationStatus.Scheduled, null, NowUtc.AddDays(1), petId: deceased),
            Row(VaccinationStatus.Applied, NowUtc.AddHours(-2), null, petId: deceased));
        _pets.Setup(r => r.ListAsync(It.IsAny<PetIdsNotReceivingCareSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid> { deceased });

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc), CancellationToken.None);

        result.Value!.Single().Kind.Should().Be(VaccinationCalendarKind.Applied);
    }

    [Fact]
    public async Task Handle_Should_Return_Both_Rows_When_Applied_And_NextDue_In_Same_Window()
    {
        Rows(Row(VaccinationStatus.Applied, NowUtc.AddHours(-1), NowUtc.AddDays(3)));

        var result = await CreateHandler().Handle(new GetVaccinationsCalendarQuery(FromUtc, ToUtc), CancellationToken.None);

        result.Value!.Select(i => i.Kind).Should().Equal(VaccinationCalendarKind.Applied, VaccinationCalendarKind.NextDue);
        result.Value!.Select(i => i.VaccinationId).Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_Should_Apply_State_Filter_On_Server()
    {
        Rows(
            Row(VaccinationStatus.Applied, NowUtc.AddHours(-1), NowUtc.AddDays(3), "Karma"),
            Row(VaccinationStatus.Scheduled, null, NowUtc.AddHours(1), "Kuduz"));

        var result = await CreateHandler().Handle(
            new GetVaccinationsCalendarQuery(FromUtc, ToUtc, States: [CalendarItemState.DueToday, CalendarItemState.Completed]),
            CancellationToken.None);

        result.Value!.Select(i => (i.Kind, i.State)).Should().Equal(
            (VaccinationCalendarKind.Applied, CalendarItemState.Completed),
            (VaccinationCalendarKind.Planned, CalendarItemState.DueToday));
    }
}
