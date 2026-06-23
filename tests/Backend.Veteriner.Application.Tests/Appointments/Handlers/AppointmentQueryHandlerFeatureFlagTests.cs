using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetById;
using Backend.Veteriner.Application.Appointments.Queries.GetCalendar;
using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class AppointmentQueryHandlerFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IAppointmentReadModelReader> _readModelReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Fact]
    public async Task List_WhenFlagFalse_Should_UseCommandRepository_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());

        var handler = CreateListHandler(false);
        await handler.Handle(new GetAppointmentsListQuery(new PageRequest()), CancellationToken.None);

        _appointments.Verify(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_UseQueryReader_NotCommandRepository()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentListReadResult(Array.Empty<AppointmentListItemDto>(), 0));

        var handler = CreateListHandler(true);
        await handler.Handle(new GetAppointmentsListQuery(new PageRequest()), CancellationToken.None);

        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _appointments.Verify(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_PassResolvedScopeToReader()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        AppointmentListReadRequest? captured = null;
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppointmentListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AppointmentListReadResult(Array.Empty<AppointmentListItemDto>(), 0));

        var handler = CreateListHandler(true);
        await handler.Handle(new GetAppointmentsListQuery(new PageRequest(), clinicId), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Scope.TenantId.Should().Be(tid);
        captured.Scope.ClinicId.Should().Be(clinicId);
    }

    [Fact]
    public async Task Calendar_WhenFlagFalse_Should_UseCommandRepository()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppointmentCalendarRow>());

        var handler = CreateCalendarHandler(false);
        await handler.Handle(new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)), CancellationToken.None);

        _appointments.Verify(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _readModelReader.Verify(r => r.GetCalendarAsync(It.IsAny<AppointmentCalendarReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Calendar_WhenFlagTrue_Should_UseQueryReader()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _readModelReader.Setup(r => r.GetCalendarAsync(It.IsAny<AppointmentCalendarReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppointmentCalendarItemDto>());

        var handler = CreateCalendarHandler(true);
        await handler.Handle(new GetAppointmentsCalendarQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)), CancellationToken.None);

        _readModelReader.Verify(r => r.GetCalendarAsync(It.IsAny<AppointmentCalendarReadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _appointments.Verify(r => r.ListAsync(It.IsAny<AppointmentsCalendarSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenQueryReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db unavailable"));

        var handler = CreateListHandler(true);
        var act = () => handler.Handle(new GetAppointmentsListQuery(new PageRequest()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _appointments.Verify(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void GetByIdHandler_Should_NotReferenceQueryReadModelsOptions()
    {
        typeof(GetAppointmentByIdQueryHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Should()
            .NotContain(typeof(IAppointmentReadModelReader));
    }

    private GetAppointmentsListQueryHandler CreateListHandler(bool enabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object,
            _readModelReader.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions { AppointmentsEnabled = enabled }));

    private GetAppointmentsCalendarQueryHandler CreateCalendarHandler(bool enabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _readModelReader.Object,
            Options.Create(new QueryReadModelsOptions { AppointmentsEnabled = enabled }));
}
