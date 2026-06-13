using Ardalis.Specification;
using Backend.Veteriner.Application.Appointments.Queries.GetById;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

public sealed class GetAppointmentByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Species>> _species = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetAppointmentByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetAppointmentByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object,
            _species.Object);

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static Appointment BuildAppointment(Guid tenantId, Guid clinicId, Guid petId, Guid? appointmentId = null)
    {
        var appt = new Appointment(
            tenantId,
            clinicId,
            petId,
            DateTime.UtcNow.AddDays(3),
            30,
            AppointmentType.Consultation,
            null,
            "not");
        if (appointmentId.HasValue)
            typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, appointmentId.Value);
        return appt;
    }

    private void SetupAppointmentFound(Appointment appt)
        => _appointments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

    private void SetupTenantWideRole(params string[] claimNames)
        => _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimNames);

    private void SetupAssignment(Guid clinicId, bool assigned)
        => _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

    private void SetupDetailGraph(Guid tenantId, Guid clinicId, Guid petId, Guid clientId)
    {
        var pet = new Pet(tenantId, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);

        var client = new Client(tenantId, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);

        var clinic = new Clinic(tenantId, "Klinik", "Adres");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, clinicId);

        var species = new Species("CAT", "Kedi", 1);
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, TestSpeciesIds.Cat);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _species.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(species);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _appointments.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Appointment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_AppointmentsNotFound_When_AppointmentMissing()
    {
        var tenantId = Guid.NewGuid();
        SetupTenant(tenantId);
        _appointments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
        _pets.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_AppointmentsNotFound_When_AppointmentBelongsToOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        SetupTenant(tenantId);
        _appointments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideUserReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        var appt = BuildAppointment(tenantId, clinicId, petId, apptId);
        SetupAppointmentFound(appt);
        SetupDetailGraph(tenantId, clinicId, petId, clientId);

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(apptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(apptId);
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-wide kullanıcı için UserClinic kontrolü çalıştırılmamalı");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_AssignedNonTenantWideUserReadsOwnClinicAppointment()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        var appt = BuildAppointment(tenantId, clinicId, petId, apptId);
        SetupAppointmentFound(appt);
        SetupDetailGraph(tenantId, clinicId, petId, clientId);

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(apptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Return_AppointmentsNotFound_When_NonTenantWideUserNotAssignedToAppointmentClinic()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: false);
        SetupAppointmentFound(BuildAppointment(tenantId, clinicId, petId, apptId));

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(apptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
        _pets.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Return_AppointmentsNotFound_When_ActiveClinicContextDiffersFromAppointmentClinic()
    {
        var tenantId = Guid.NewGuid();
        var appointmentClinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupTenantWideRole("Admin");
        SetupAppointmentFound(BuildAppointment(tenantId, appointmentClinicId, petId, apptId));

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(apptId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Appointments.NotFound");
        _userOperationClaims.Verify(
            x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_With_ClientId_Species_And_AppointmentType_When_Found()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        SetupTenant(tid);
        SetupTenantWideRole("Owner");
        var appt = BuildAppointment(tid, cid, petId, apptId);
        SetupAppointmentFound(appt);
        SetupDetailGraph(tid, cid, petId, clientId);

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(apptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClientId.Should().Be(clientId);
        result.Value.SpeciesId.Should().Be(TestSpeciesIds.Cat);
        result.Value.SpeciesName.Should().Be("Kedi");
        result.Value.AppointmentType.Should().Be(AppointmentType.Consultation);
        result.Value.ClientName.Should().Be("Ali Veli");
        result.Value.PetName.Should().Be("Pamuk");
        result.Value.Notes.Should().Be("not");
        result.Value.DurationMinutes.Should().Be(30);
        result.Value.ScheduledEndUtc.Should().Be(appt.ScheduledEndUtc);
    }

    [Fact]
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupAppointmentFound(BuildAppointment(tenantId, clinicId, petId, apptId));
        SetupDetailGraph(tenantId, clinicId, petId, Guid.NewGuid());

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetAppointmentByIdQuery(apptId), token);

        _appointments.Verify(x => x.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }
}
