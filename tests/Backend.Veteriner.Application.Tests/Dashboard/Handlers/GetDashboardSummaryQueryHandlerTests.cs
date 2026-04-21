using Backend.Veteriner.Application.Tests;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Queries.GetSummary;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Dashboard.Handlers;

public sealed class GetDashboardSummaryQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IClinicContext> _clinic = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IDashboardTodayAppointmentStatusCountsReader> _todayAppointmentCounts = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IDashboardClinicScopedReader> _clinicScoped = new();

    private GetDashboardSummaryQueryHandler CreateHandler()
        => new(
            _tenant.Object,
            _clinic.Object,
            _appointments.Object,
            _todayAppointmentCounts.Object,
            _clients.Object,
            _pets.Object,
            _clinicScoped.Object);

    /// <summary>
    /// Handler trend projeksiyonu için `ListAsync(DashboardAppointmentScheduledAtInWindowSpec)` çağrısı yapar;
    /// Moq loose behavior'da default Task bağlamında ListAsync null döner ve foreach NRE atar.
    /// Her test bu setup'ı beklentiyle override edebilir; default olarak boş liste.
    /// </summary>
    private void SetupEmptyAppointmentTrend()
    {
        _appointments
            .Setup(a => a.ListAsync(It.IsAny<DashboardAppointmentScheduledAtInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DateTime>());
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenant.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _appointments.Verify(
            a => a.CountAsync(It.IsAny<ISpecification<Appointment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnZeros_When_NoData()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());
        SetupEmptyAppointmentTrend();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TodayAppointmentsCount.Should().Be(0);
        result.Value.UpcomingAppointmentsCount.Should().Be(0);
        result.Value.CompletedTodayCount.Should().Be(0);
        result.Value.CancelledTodayCount.Should().Be(0);
        result.Value.TotalClientsCount.Should().Be(0);
        result.Value.TotalPetsCount.Should().Be(0);
        result.Value.UpcomingAppointments.Should().BeEmpty();
        result.Value.RecentClients.Should().BeEmpty();
        result.Value.RecentPets.Should().BeEmpty();
        result.Value.Last7DaysAppointments.Should().HaveCount(7);
        result.Value.Last7DaysAppointments.Should().OnlyContain(d => d.Count == 0);
    }

    [Fact]
    public async Task Handle_Should_MapCounts_And_Lists()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(4, 1, 2));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        var when = DateTime.UtcNow.AddHours(2);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), when, AppointmentType.Other, null, null);
        var apptRow = new DashboardUpcomingAppointmentRow(
            appt.Id, appt.ClinicId, appt.PetId, appt.ScheduledAtUtc, appt.Status);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow> { apptRow });

        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var client = new Client(tid, "Ali Veli", "05321111111");
        var clientRow = new DashboardRecentClientRow(client.Id, client.FullName, client.Phone);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow> { clientRow });

        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        var pet = new Pet(tid, client.Id, "Pamuk", TestSpeciesIds.Cat);
        var petRow = new DashboardRecentPetRow(pet.Id, pet.ClientId, pet.Name, "Cat");
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow> { petRow });
        SetupEmptyAppointmentTrend();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var v = result.Value!;
        v.TodayAppointmentsCount.Should().Be(4);
        v.UpcomingAppointmentsCount.Should().Be(10);
        v.CompletedTodayCount.Should().Be(1);
        v.CancelledTodayCount.Should().Be(2);
        v.TotalClientsCount.Should().Be(7);
        v.TotalPetsCount.Should().Be(5);
        v.UpcomingAppointments.Should().ContainSingle(x => x.Id == appt.Id && x.ScheduledAtUtc == appt.ScheduledAtUtc);
        v.RecentClients.Should().ContainSingle(x => x.Id == client.Id && x.FullName == "Ali Veli");
        v.RecentPets.Should().ContainSingle(x => x.Id == pet.Id && x.Name == "Pamuk");
    }

    [Fact]
    public async Task Handle_Should_InvokeExpectedAppointmentSpecs_When_Success()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());
        SetupEmptyAppointmentTrend();

        var handler = CreateHandler();
        await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        _todayAppointmentCounts.Verify(
            r => r.GetAsync(tid, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _appointments.Verify(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()), Times.Once);

        _clinicScoped.Verify(r => r.CountClientsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicScoped.Verify(r => r.CountPetsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicScoped.Verify(r => r.ListRecentClientsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicScoped.Verify(r => r.ListRecentPetsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_UseClinicScopedReader_When_ClinicSelected()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, cid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(2, 1, 0));
        _appointments
            .Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _appointments
            .Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());

        var clinicClient = new Client(tid, "Klinik Müşterisi", "05329998877");
        var clinicPet = new Pet(tid, clinicClient.Id, "Minnoş", TestSpeciesIds.Cat);

        _clinicScoped
            .Setup(r => r.CountClientsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        _clinicScoped
            .Setup(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(22);
        _clinicScoped
            .Setup(r => r.ListRecentClientsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>
            {
                new(clinicClient.Id, clinicClient.FullName, clinicClient.Phone),
            });
        _clinicScoped
            .Setup(r => r.ListRecentPetsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>
            {
                new(clinicPet.Id, clinicPet.ClientId, clinicPet.Name, "Cat"),
            });
        SetupEmptyAppointmentTrend();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var v = result.Value!;
        v.TotalClientsCount.Should().Be(11);
        v.TotalPetsCount.Should().Be(22);
        v.RecentClients.Should().ContainSingle(x => x.Id == clinicClient.Id);
        v.RecentPets.Should().ContainSingle(x => x.Id == clinicPet.Id);

        _clients.Verify(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _clients.Verify(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _pets.Verify(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _pets.Verify(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()), Times.Never);

        _clinicScoped.Verify(r => r.CountClientsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()), Times.Once);
        _clinicScoped.Verify(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()), Times.Once);
        _clinicScoped.Verify(r => r.ListRecentClientsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _clinicScoped.Verify(r => r.ListRecentPetsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_NarrowTotalClientsCount_ByClinic_When_ClinicSelected()
    {
        var (tid, cid) = SetupClinicScopedZeros();
        _clinicScoped
            .Setup(r => r.CountClientsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalClientsCount.Should().Be(7);
        _clients.Verify(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_NarrowTotalPetsCount_ByClinic_When_ClinicSelected()
    {
        var (tid, cid) = SetupClinicScopedZeros();
        _clinicScoped
            .Setup(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalPetsCount.Should().Be(5);
        _pets.Verify(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_NarrowRecentClients_ByClinic_When_ClinicSelected()
    {
        var (tid, cid) = SetupClinicScopedZeros();
        var client = new Client(tid, "Klinik Veli", "05329998877");
        _clinicScoped
            .Setup(r => r.ListRecentClientsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>
            {
                new(client.Id, client.FullName, client.Phone),
            });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecentClients.Should().ContainSingle(x => x.Id == client.Id);
        _clients.Verify(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_NarrowRecentPets_ByClinic_When_ClinicSelected()
    {
        var (tid, cid) = SetupClinicScopedZeros();
        var pet = new Pet(tid, Guid.NewGuid(), "Boncuk", TestSpeciesIds.Dog);
        _clinicScoped
            .Setup(r => r.ListRecentPetsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>
            {
                new(pet.Id, pet.ClientId, pet.Name, "Dog"),
            });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.RecentPets.Should().ContainSingle(x => x.Id == pet.Id);
        _pets.Verify(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_KeepTenantWideFallback_When_NoClinicSelected()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(17);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());
        SetupEmptyAppointmentTrend();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalClientsCount.Should().Be(42);
        result.Value!.TotalPetsCount.Should().Be(17);

        _clinicScoped.Verify(r => r.CountClientsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicScoped.Verify(r => r.CountPetsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicScoped.Verify(r => r.ListRecentClientsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicScoped.Verify(r => r.ListRecentPetsAtClinicAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private (Guid tid, Guid cid) SetupClinicScopedZeros()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, cid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments
            .Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments
            .Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _clinicScoped
            .Setup(r => r.CountClientsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clinicScoped
            .Setup(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clinicScoped
            .Setup(r => r.ListRecentClientsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _clinicScoped
            .Setup(r => r.ListRecentPetsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());
        SetupEmptyAppointmentTrend();
        return (tid, cid);
    }

    [Fact]
    public async Task Handle_Should_FillLast7DaysAppointments_WithZeros_WhenNoData()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());
        SetupEmptyAppointmentTrend();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var trend = result.Value!.Last7DaysAppointments;
        trend.Should().HaveCount(7);
        trend.Should().OnlyContain(d => d.Count == 0);
        trend.Select(d => d.Date).Should().BeInAscendingOrder();
        // Bugün dahil: son elemanın tarihi İstanbul'un bugünü olmalı.
        trend[^1].Date.Should().Be(GetIstanbulToday());
        trend[0].Date.Should().Be(GetIstanbulToday().AddDays(-6));
    }

    [Fact]
    public async Task Handle_Should_BucketAppointmentTrend_ByIstanbulDay()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());

        var buckets = OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow);
        // 3 timestamp → bugün bucket; 1 timestamp → 3 gün öncesi bucket.
        var today = buckets[6];
        var threeDaysAgo = buckets[3];
        var scheduledTimes = new List<DateTime>
        {
            today.StartUtcInclusive.AddHours(1),
            today.StartUtcInclusive.AddHours(5),
            today.StartUtcInclusive.AddHours(23).AddMinutes(59),
            threeDaysAgo.StartUtcInclusive.AddHours(12),
        };
        _appointments
            .Setup(a => a.ListAsync(It.IsAny<DashboardAppointmentScheduledAtInWindowSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scheduledTimes);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var trend = result.Value!.Last7DaysAppointments;
        trend.Should().HaveCount(7);
        trend[6].Count.Should().Be(3);
        trend[3].Count.Should().Be(1);
        trend.Where((_, i) => i != 3 && i != 6).Should().OnlyContain(d => d.Count == 0);
        trend[6].Date.Should().Be(today.LocalDate);
        trend[0].Date.Should().Be(buckets[0].LocalDate);
    }

    [Fact]
    public async Task Handle_Should_UseClinicFilter_ForAppointmentTrend_When_ClinicSelected()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);
        _clinic.SetupGet(c => c.ClinicId).Returns(cid);

        _todayAppointmentCounts
            .Setup(r => r.GetAsync(tid, cid, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardTodayAppointmentStatusCounts(0, 0, 0));
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardUpcomingAppointmentRow>());
        _clinicScoped.Setup(r => r.CountClientsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _clinicScoped.Setup(r => r.CountPetsAtClinicAsync(tid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _clinicScoped.Setup(r => r.ListRecentClientsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentClientRow>());
        _clinicScoped.Setup(r => r.ListRecentPetsAtClinicAsync(tid, cid, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DashboardRecentPetRow>());
        SetupEmptyAppointmentTrend();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Last7DaysAppointments.Should().HaveCount(7);
        _appointments.Verify(
            a => a.ListAsync(It.IsAny<DashboardAppointmentScheduledAtInWindowSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static DateOnly GetIstanbulToday()
    {
        var buckets = OperationPeriodBounds.Last7DaysForUtcNow(DateTime.UtcNow);
        return buckets[^1].LocalDate;
    }
}
