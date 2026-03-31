using Backend.Veteriner.Application.Tests;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
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
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();

    private GetDashboardSummaryQueryHandler CreateHandler()
        => new(_tenant.Object, _clinic.Object, _appointments.Object, _clients.Object, _pets.Object);

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
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayCompletedCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayCancelledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

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
    }

    [Fact]
    public async Task Handle_Should_MapCounts_And_Lists()
    {
        var tid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayCompletedCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayCancelledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var when = DateTime.UtcNow.AddHours(2);
        var appt = new Appointment(tid, Guid.NewGuid(), Guid.NewGuid(), when, AppointmentType.Other, null, null);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { appt });

        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var client = new Client(tid, "Ali Veli", "05321111111");
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client> { client });

        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        var pet = new Pet(tid, client.Id, "Pamuk", TestSpeciesIds.Cat);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { pet });

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

        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayCompletedCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.CountAsync(It.IsAny<DashboardTodayCancelledCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
        _clients.Setup(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _clients.Setup(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var handler = CreateHandler();
        await handler.Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        _appointments.Verify(a => a.CountAsync(It.IsAny<DashboardTodayScheduledCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(a => a.CountAsync(It.IsAny<DashboardUpcomingScheduledCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(a => a.CountAsync(It.IsAny<DashboardTodayCompletedCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(a => a.CountAsync(It.IsAny<DashboardTodayCancelledCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(a => a.ListAsync(It.IsAny<DashboardUpcomingScheduledListSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.CountAsync(It.IsAny<DashboardClientsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _clients.Verify(c => c.ListAsync(It.IsAny<DashboardRecentClientsListSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(p => p.CountAsync(It.IsAny<DashboardPetsTotalCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(p => p.ListAsync(It.IsAny<DashboardRecentPetsListSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
