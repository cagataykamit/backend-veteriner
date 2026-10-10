using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Visits.Commands.Create;
using Backend.Veteriner.Application.Visits.IntegrationEvents;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Visits;

public sealed class CreateVisitCommandHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Visit>> _visitsRead = new();
    private readonly Mock<IRepository<Visit>> _visitsWrite = new();
    private readonly Mock<IVisitIntegrationEventOutbox> _outbox = new();

    public CreateVisitCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(c => c.UserId).Returns(_userId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(_tenantId, "K", "Istanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(_tenantId, Guid.NewGuid(), "Pamuk", Guid.NewGuid()));
    }

    private CreateVisitCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _scopeResolver.Object,
            _userClinics.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointments.Object,
            _visitsRead.Object,
            _visitsWrite.Object,
            _outbox.Object,
            VisitHandlerTestSupport.FixedClock);

    private Appointment ScheduledAppointment()
        => new(_tenantId, _clinicId, _petId, DateTime.UtcNow.AddHours(1), 30, AppointmentType.Other, null, null);

    private void ReturnAppointment(Appointment appointment)
        => _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);

    private void ReturnVisitForAppointment(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByAppointmentIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    private void ReturnActiveVisitForPet(Visit? visit)
        => _visitsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ActiveVisitByPetIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Walkin_Should_Fail_Validation_When_Pet_Missing()
    {
        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, null, null, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
        _visitsWrite.Verify(r => r.AddAsync(It.IsAny<Visit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Walkin_Should_Create_Visit_Without_Appointment_And_Enqueue_Event()
    {
        ReturnActiveVisitForPet(null);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeTrue();
        result.Value.Visit.AppointmentId.Should().BeNull();
        result.Value.Visit.CareStatus.Should().Be(VisitCareStatus.Waiting);
        result.Value.Visit.ArrivedAtUtc.Should().Be(VisitHandlerTestSupport.FixedNowUtc);
        result.Value.Visit.CreatedByUserId.Should().Be(_userId);
        _visitsWrite.Verify(r => r.AddAsync(It.IsAny<Visit>(), It.IsAny<CancellationToken>()), Times.Once);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(
            o => o.EnqueueAsync(
                VisitIntegrationEventTypes.Created,
                It.IsAny<VisitCreatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _appointments.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Walkin_Should_Return_Existing_Active_Visit_Without_Creating()
    {
        var existing = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, _petId);
        ReturnActiveVisitForPet(existing);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeFalse();
        result.Value.Visit.Id.Should().Be(existing.Id);
        _visitsWrite.Verify(r => r.AddAsync(It.IsAny<Visit>(), It.IsAny<CancellationToken>()), Times.Never);
        _visitsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _outbox.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Appointment_Should_Derive_Clinic_And_Pet_From_Appointment()
    {
        var appointment = ScheduledAppointment();
        ReturnAppointment(appointment);
        ReturnVisitForAppointment(null);
        ReturnActiveVisitForPet(null);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(null, null, appointment.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeTrue();
        result.Value.Visit.AppointmentId.Should().Be(appointment.Id);
        result.Value.Visit.ClinicId.Should().Be(_clinicId);
        result.Value.Visit.PetId.Should().Be(_petId);
    }

    [Fact]
    public async Task Handle_Appointment_Should_Return_Existing_Visit_Even_When_Appointment_Completed()
    {
        var appointment = ScheduledAppointment();
        appointment.Complete();
        ReturnAppointment(appointment);
        var existing = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, _petId, appointment.Id);
        ReturnVisitForAppointment(existing);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(null, null, appointment.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeFalse();
        result.Value.Visit.Id.Should().Be(existing.Id);
        _visitsWrite.Verify(r => r.AddAsync(It.IsAny<Visit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Appointment_Should_Fail_When_Cancelled()
    {
        var appointment = ScheduledAppointment();
        appointment.Cancel();
        ReturnAppointment(appointment);
        ReturnVisitForAppointment(null);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(null, null, appointment.Id, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.AppointmentCancelled");
    }

    [Fact]
    public async Task Handle_Appointment_Should_Fail_When_Completed_Without_Visit()
    {
        var appointment = ScheduledAppointment();
        appointment.Complete();
        ReturnAppointment(appointment);
        ReturnVisitForAppointment(null);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(null, null, appointment.Id, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.AppointmentNotScheduled");
    }

    [Fact]
    public async Task Handle_Appointment_Should_Fail_When_Pet_Differs_From_Appointment()
    {
        var appointment = ScheduledAppointment();
        ReturnAppointment(appointment);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(null, Guid.NewGuid(), appointment.Id, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.AppointmentPetClinicMismatch");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Request_Clinic_Differs_From_Clinic_Context()
    {
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, null), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.ClinicContextMismatch");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Not_Assigned_To_User()
    {
        _scopeResolver.SetupAccessDenied();

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, null), CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Responsible_Veterinarian_Not_Assigned_To_Clinic()
    {
        ReturnActiveVisitForPet(null);
        var vetId = Guid.NewGuid();
        _userClinics.Setup(r => r.ExistsActiveInTenantAsync(vetId, _tenantId, _clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, vetId), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.Validation");
        _visitsWrite.Verify(r => r.AddAsync(It.IsAny<Visit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_Winner_When_Unique_Index_Rejects_Concurrent_Insert()
    {
        var winner = VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, _petId);
        _visitsRead.SetupSequence(r => r.FirstOrDefaultAsync(It.IsAny<ActiveVisitByPetIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Visit?)null)
            .ReturnsAsync(winner);
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("duplicate"));

        var result = await CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().BeFalse();
        result.Value.Visit.Id.Should().Be(winner.Id);
    }

    [Fact]
    public async Task Handle_Should_Rethrow_When_Save_Fails_And_No_Winner_Exists()
    {
        ReturnActiveVisitForPet(null);
        _visitsWrite.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("other failure"));

        var act = () => CreateHandler().Handle(
            new CreateVisitCommand(_clinicId, _petId, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
