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
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IReadRepository<Species>> _species = new();

    private GetAppointmentByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object,
            _species.Object);

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
    public async Task Handle_Should_ReturnDetail_With_ClientId_Species_And_AppointmentType_When_Found()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var apptId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);

        var appt = new Appointment(tid, cid, petId, DateTime.UtcNow.AddDays(1), AppointmentType.Consultation, null, "not");
        typeof(Appointment).GetProperty(nameof(Appointment.Id))!.SetValue(appt, apptId);

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);

        var clinic = new Clinic(tid, "Klinik", "Adres");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);

        var species = new Species("CAT", "Kedi", 1);
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, TestSpeciesIds.Cat);

        _appointments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<AppointmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _clinics.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _species.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(species);

        var result = await CreateHandler().Handle(new GetAppointmentByIdQuery(apptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClientId.Should().Be(clientId);
        result.Value.SpeciesId.Should().Be(TestSpeciesIds.Cat);
        result.Value.SpeciesName.Should().Be("Kedi");
        result.Value.AppointmentType.Should().Be(AppointmentType.Consultation);
        result.Value.ClientName.Should().Be("Ali Veli");
        result.Value.PetName.Should().Be("Pamuk");
        result.Value.Notes.Should().Be("not");
    }
}
