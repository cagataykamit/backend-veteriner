using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPetVaccinations;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>
/// Portal aşı ucu: ADR-028 gün bazlı gecikme (İstanbul günü, <c>VaccinationDueRule</c>), vefat eden hayvanda yaklaşan/gecikmiş
/// liste yok, iptal ve uygulanan kayıt geçmişte, sahiplik dışı hayvan <c>Portal.Pets.NotFound</c>.
/// </summary>
public sealed class GetPortalPetVaccinationsQueryHandlerTests
{
    private readonly Tenant _tenant = new("Portal Kiracı", DateTime.UtcNow);
    private readonly OwnerAccount _account = OwnerAccount.Create("ayse@example.com", "Ayşe", OwnerPortalTestFixture.Now);
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly OwnerAccountClientLink _link;
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Vaccination>> _vaccinations = new();
    private readonly List<PortalVaccinationRow> _rows = [];
    private Pet? _pet;

    public GetPortalPetVaccinationsQueryHandlerTests()
    {
        _link = OwnerAccountClientLink.Create(_account.Id, _tenant.Id, Guid.NewGuid(), _clientId, Guid.NewGuid(), OwnerPortalTestFixture.Now);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _pet);
        _vaccinations.Setup(r => r.ListAsync(It.IsAny<ISpecification<Vaccination, PortalVaccinationRow>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _rows.ToList());
    }

    private GetPortalPetVaccinationsQueryHandler Handler(DateTime utcNow)
    {
        var accounts = new Mock<IReadRepository<OwnerAccount>>();
        accounts.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<OwnerAccount>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_account);
        var links = new Mock<IReadRepository<OwnerAccountClientLink>>();
        links.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<OwnerAccountClientLink>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_link);
        var tenants = new Mock<IReadRepository<Tenant>>();
        tenants.Setup(r => r.ListAsync(It.IsAny<ISpecification<Tenant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_tenant]);

        var access = new OwnerPortalAccess(
            accounts.Object, links.Object, tenants.Object,
            new Mock<IReadRepository<Clinic>>().Object, Mock.Of<ITenantScope>(), new Mock<IReadRepository<Client>>().Object);
        return new GetPortalPetVaccinationsQueryHandler(
            access, _pets.Object, _vaccinations.Object, new OwnerPortalTestFixture.FixedClock(utcNow));
    }

    private Pet GivenPet(bool deceased = false)
    {
        _pet = new Pet(_tenant.Id, _clientId, "Pamuk", Guid.NewGuid());
        if (deceased)
            _pet.SetStatus(PetStatus.Deceased, null, new DateOnly(2026, 10, 1)).IsSuccess.Should().BeTrue();
        return _pet;
    }

    private Guid Scheduled(string name, DateTime? dueAtUtc)
    {
        var id = Guid.NewGuid();
        _rows.Add(new PortalVaccinationRow(id, name, VaccinationStatus.Scheduled, null, dueAtUtc, OwnerPortalTestFixture.Now));
        return id;
    }

    private ValueTask<Result<PortalPetVaccinationsDto>> SendAsync(DateTime utcNow)
        => Handler(utcNow).Handle(new GetPortalPetVaccinationsQuery(_account.Id, _link.Id, _pet?.Id ?? Guid.NewGuid()), CancellationToken.None);

    [Fact]
    public async Task Overdue_Should_Use_Istanbul_Day_Start_Not_Clock_Time()
    {
        GivenPet();
        // 16.10.2026 12:00 İstanbul (UTC+3) → gün başı 15.10.2026 21:00 UTC.
        var now = new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
        var yesterdayLastSecond = Scheduled("Dün", new DateTime(2026, 10, 15, 20, 59, 59, DateTimeKind.Utc));
        var todayStart = Scheduled("Bugün başı", new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc));
        var todayPassedHour = Scheduled("Bugün saati geçmiş", new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc));
        var undated = Scheduled("Vadesiz", null);

        var result = await SendAsync(now);

        result.IsSuccess.Should().BeTrue();
        var upcoming = result.Value!.Upcoming;
        upcoming.Select(v => v.VaccinationId).Should().Equal(yesterdayLastSecond, todayStart, todayPassedHour, undated);
        upcoming.Single(v => v.VaccinationId == yesterdayLastSecond).IsOverdue.Should().BeTrue();
        upcoming.Single(v => v.VaccinationId == todayStart).IsOverdue.Should().BeFalse();
        upcoming.Single(v => v.VaccinationId == todayPassedHour).IsOverdue.Should().BeFalse("bugün vadesi olan aşı saati geçse de gecikmiş değildir");
        upcoming.Single(v => v.VaccinationId == undated).IsOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task After_Midnight_In_Istanbul_Previous_Utc_Date_Due_Should_Be_Overdue()
    {
        GivenPet();
        // 16.10.2026 01:30 İstanbul = 15.10.2026 22:30 UTC; 15.10 23:00 İstanbul vadesi artık dünkü gündür.
        var now = new DateTime(2026, 10, 15, 22, 30, 0, DateTimeKind.Utc);
        var id = Scheduled("Karma", new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc));

        var result = await SendAsync(now);

        result.Value!.Upcoming.Single(v => v.VaccinationId == id).IsOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task Deceased_Pet_Should_Have_History_But_No_Upcoming_Or_Overdue()
    {
        GivenPet(deceased: true);
        Scheduled("Gecikmiş", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        var applied = Guid.NewGuid();
        _rows.Add(new PortalVaccinationRow(applied, "Kuduz", VaccinationStatus.Applied, new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc), null, OwnerPortalTestFixture.Now));

        var result = await SendAsync(OwnerPortalTestFixture.Now);

        result.Value!.Upcoming.Should().BeEmpty("vefat eden hastanın aşısı ne gecikmiş ne yaklaşan sayılır (ADR-028)");
        result.Value.History.Select(h => h.VaccinationId).Should().Equal(applied);
    }

    [Fact]
    public async Task History_Should_Contain_Applied_And_Cancelled_Newest_First()
    {
        GivenPet();
        var olderApplied = Guid.NewGuid();
        var cancelled = Guid.NewGuid();
        var newerApplied = Guid.NewGuid();
        _rows.Add(new PortalVaccinationRow(olderApplied, "Kuduz", VaccinationStatus.Applied, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), null, OwnerPortalTestFixture.Now));
        _rows.Add(new PortalVaccinationRow(cancelled, "Karma", VaccinationStatus.Cancelled, null, new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc), OwnerPortalTestFixture.Now));
        _rows.Add(new PortalVaccinationRow(newerApplied, "Karma", VaccinationStatus.Applied, new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), null, OwnerPortalTestFixture.Now));

        var result = await SendAsync(OwnerPortalTestFixture.Now);

        result.Value!.History.Select(h => h.VaccinationId).Should().Equal(newerApplied, cancelled, olderApplied);
        result.Value.History.Single(h => h.VaccinationId == cancelled).Status.Should().Be(VaccinationStatus.Cancelled);
        result.Value.Upcoming.Should().BeEmpty();
    }

    [Fact]
    public async Task Pet_Not_Of_Link_Client_Should_Be_Not_Found()
    {
        _pet = null;

        var result = await SendAsync(OwnerPortalTestFixture.Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Portal.Pets.NotFound");
        _vaccinations.Verify(
            r => r.ListAsync(It.IsAny<ISpecification<Vaccination, PortalVaccinationRow>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
