using Backend.Veteriner.Application.Appointments.Queries.GetCalendar;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class GetAppointmentsCalendarQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetAppointmentsCalendarQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var query = new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1));

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateWindowMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var query = new GetAppointmentsCalendarQuery(null, DateTime.UtcNow.AddDays(1));

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.Calendar.DateWindowRequired");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateWindowInvalid()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var now = DateTime.UtcNow;
        var query = new GetAppointmentsCalendarQuery(now, now);

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.Calendar.InvalidDateWindow");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_DateWindowExceedsLimit()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        var query = new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(46));

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.Calendar.DateWindowTooLarge");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_QueryClinic_Differs_From_ActiveClinicContext()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        var query = new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), ClinicId: Guid.NewGuid());

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_ReturnEmpty_When_NoRows()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_MapRows_WithPetAndClientNames()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);

        var rows = new List<AppointmentCalendarRow>
        {
            new(Guid.NewGuid(), clinicId, petId, DateTime.UtcNow.AddHours(1), AppointmentStatus.Scheduled, AppointmentType.Consultation),
            new(Guid.NewGuid(), clinicId, petId, DateTime.UtcNow.AddHours(2), AppointmentStatus.Cancelled, AppointmentType.Other),
        };
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsNameClientSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetNameClientRow> { new(petId, clientId, "Pamuk") });
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantIdsNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClientNameRow> { new(clientId, "Ali Veli") });

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), Status: AppointmentStatus.Scheduled),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].PetName.Should().Be("Pamuk");
        result.Value[0].ClientName.Should().Be("Ali Veli");
        result.Value[0].ClientId.Should().Be(clientId);
    }

    [Fact]
    public async Task Handle_Should_UseActiveClinicContext_When_ClinicIdNotProvided()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var result = await CreateHandler().Handle(
            new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _appointments.Verify(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
