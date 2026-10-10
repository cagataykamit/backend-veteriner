using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Commands.Create;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Tests.Visits;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Visits;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class CreateExaminationWithVisitTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _petId = Guid.NewGuid();

    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IRepository<Appointment>> _appointmentsWrite = new();
    private readonly Mock<IRepository<Examination>> _examinationsWrite = new();
    private readonly Mock<IReadRepository<Visit>> _visits = new();

    public CreateExaminationWithVisitTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(_tenantId, "K", "Istanbul"));
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(_tenantId, Guid.NewGuid(), "Pamuk", Guid.NewGuid()));
    }

    private CreateExaminationCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _tenants.Object,
            _clinics.Object,
            _pets.Object,
            _appointments.Object,
            _appointmentsWrite.Object,
            _examinationsWrite.Object,
            _visits.Object,
            VisitHandlerTestSupport.FixedClock);

    private static CreateExaminationCommand VisitOnly(
        Guid visitId, Guid? petId = null, Guid? appointmentId = null, Guid? clinicId = null)
        => new(clinicId, petId, appointmentId, DateTime.UtcNow.AddHours(-1), "Sikayet", "Bulgu", null, null, VisitId: visitId);

    private Visit WaitingVisit(Guid? appointmentId = null)
        => VisitHandlerTestSupport.NewVisit(_tenantId, _clinicId, _petId, appointmentId);

    private void ReturnVisit(Visit? visit)
        => _visits.Setup(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);

    [Fact]
    public async Task Handle_Should_Derive_Pet_And_Clinic_From_Visit_And_Start_Waiting_Visit()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        Examination? saved = null;
        _examinationsWrite.Setup(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()))
            .Callback<Examination, CancellationToken>((e, _) => saved = e)
            .ReturnsAsync((Examination e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(VisitOnly(visit.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.VisitId.Should().Be(visit.Id);
        saved.PetId.Should().Be(_petId);
        saved.ClinicId.Should().Be(_clinicId);
        visit.CareStatus.Should().Be(VisitCareStatus.InProgress);
        _examinationsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Not_Touch_Visit_Already_InProgress()
    {
        var visit = WaitingVisit();
        visit.Start(VisitHandlerTestSupport.FixedNowUtc);
        var sequence = visit.MutationSequence;
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(VisitOnly(visit.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        visit.MutationSequence.Should().Be(sequence);
    }

    [Fact]
    public async Task Handle_Should_Inherit_Appointment_From_Visit_And_Keep_K2A_Completion()
    {
        var appointment = new Appointment(
            _tenantId, _clinicId, _petId, DateTime.UtcNow.AddHours(1), 30, AppointmentType.Other, null, null);
        var visit = WaitingVisit(appointment.Id);
        ReturnVisit(visit);
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        Examination? saved = null;
        _examinationsWrite.Setup(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()))
            .Callback<Examination, CancellationToken>((e, _) => saved = e)
            .ReturnsAsync((Examination e, CancellationToken _) => e);

        var result = await CreateHandler().Handle(VisitOnly(visit.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        saved!.AppointmentId.Should().Be(appointment.Id);
        appointment.Status.Should().Be(AppointmentStatus.Completed);
        _appointmentsWrite.Verify(r => r.UpdateAsync(appointment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Fail_NotFound_When_Visit_Missing()
    {
        ReturnVisit(null);

        var result = await CreateHandler().Handle(VisitOnly(Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotFound");
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_NotOpen_When_Visit_Completed()
    {
        var visit = WaitingVisit();
        visit.Start(VisitHandlerTestSupport.FixedNowUtc);
        visit.Complete(VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(VisitOnly(visit.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotOpen");
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_NotOpen_When_Visit_Voided()
    {
        var visit = WaitingVisit();
        visit.MarkAsMistaken("Yanlis gelis kaydi", VisitHandlerTestSupport.FixedNowUtc);
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(VisitOnly(visit.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotOpen");
    }

    [Fact]
    public async Task Handle_Should_Fail_VisitMismatch_When_Pet_Differs_From_Visit()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);

        var result = await CreateHandler().Handle(
            VisitOnly(visit.Id, petId: Guid.NewGuid()), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.VisitMismatch");
        _examinationsWrite.Verify(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_VisitMismatch_When_Appointment_Given_But_Visit_Has_None()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        var appointment = new Appointment(
            _tenantId, _clinicId, _petId, DateTime.UtcNow.AddHours(1), 30, AppointmentType.Other, null, null);
        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);

        var result = await CreateHandler().Handle(
            VisitOnly(visit.Id, appointmentId: appointment.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Examinations.VisitMismatch");
    }

    [Fact]
    public async Task Handle_Should_Return_NotFound_When_Visit_Belongs_To_Other_Clinic_Context()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

        var result = await CreateHandler().Handle(VisitOnly(visit.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Return_ConcurrencyConflict_When_Visit_Changed_Concurrently()
    {
        var visit = WaitingVisit();
        ReturnVisit(visit);
        _examinationsWrite.Setup(r => r.AddAsync(It.IsAny<Examination>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(VisitOnly(visit.Id), CancellationToken.None);

        result.Error.Code.Should().Be("Visits.ConcurrencyConflict");
    }

    [Fact]
    public async Task Handle_Without_Visit_Should_Not_Query_Visits()
    {
        var command = new CreateExaminationCommand(
            _clinicId, _petId, null, DateTime.UtcNow.AddHours(-1), "Sikayet", "Bulgu", null, null);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _visits.Verify(r => r.FirstOrDefaultAsync(It.IsAny<VisitByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
