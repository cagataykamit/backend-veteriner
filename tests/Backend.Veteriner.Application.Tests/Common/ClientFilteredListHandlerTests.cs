using Ardalis.Specification;
using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Vaccinations;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>Randevu/muayene/aşı listelerinde <c>clientId</c> süzgecinin handler davranışı.</summary>
public sealed class ClientFilteredListHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private static readonly PageRequest Page = new() { Page = 1, PageSize = 20 };

    public ClientFilteredListHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(_clinicId);
    }

    private void SetupClientPets(params Pet[] pets)
        => _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pets.ToList());

    [Fact]
    public async Task Examinations_ClientWithoutPets_Should_Return_Empty_Without_Querying()
    {
        var examinations = new Mock<IReadRepository<Examination>>();
        SetupClientPets();
        var handler = new GetExaminationsListQueryHandler(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, examinations.Object,
            _pets.Object, _clients.Object, _petLookupReader.Object, Options.Create(new QueryReadModelsOptions()));

        var result = await handler.Handle(new GetExaminationsListQuery(Page, ClientId: Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        examinations.Verify(r => r.CountAsync(It.IsAny<ISpecification<Examination>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Examinations_ClientWithPets_Should_Query_With_Client_Filter()
    {
        var examinations = new Mock<IReadRepository<Examination>>();
        var clientId = Guid.NewGuid();
        var pet = new Pet(_tenantId, clientId, "Pamuk", Guid.NewGuid());
        SetupClientPets(pet);
        examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());
        var handler = new GetExaminationsListQueryHandler(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, examinations.Object,
            _pets.Object, _clients.Object, _petLookupReader.Object, Options.Create(new QueryReadModelsOptions()));

        var result = await handler.Handle(new GetExaminationsListQuery(Page, ClientId: clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        examinations.Verify(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Vaccinations_ClientWithoutPets_Should_Return_Empty_Without_Querying()
    {
        var vaccinations = new Mock<IReadRepository<Vaccination>>();
        SetupClientPets();
        var handler = new GetVaccinationsListQueryHandler(
            _tenantContext.Object, _clinicContext.Object, _scopeResolver.Object, vaccinations.Object,
            new Mock<IReadRepository<Administration>>().Object, _pets.Object, _clients.Object, _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions()),
            TimeProvider.System);

        var result = await handler.Handle(new GetVaccinationsListQuery(Page, ClientId: Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        vaccinations.Verify(r => r.CountAsync(It.IsAny<ISpecification<Vaccination>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Appointments_CommandPath_ClientWithoutPets_Should_Return_Empty_Without_Querying()
    {
        var appointments = new Mock<IReadRepository<Appointment>>();
        SetupClientPets();
        var handler = CreateAppointmentsHandler(appointments, new Mock<IAppointmentReadModelReader>(), appointmentsEnabled: false);

        var result = await handler.Handle(new GetAppointmentsListQuery(Page, ClientId: Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(0);
        appointments.Verify(r => r.CountAsync(It.IsAny<ISpecification<Appointment>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Appointments_ReadModelPath_Should_Pass_ClientId_To_Reader()
    {
        var reader = new Mock<IAppointmentReadModelReader>();
        AppointmentListReadRequest? captured = null;
        reader.Setup(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppointmentListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new AppointmentListReadResult(new List<AppointmentListItemDto>(), 0));
        var clientId = Guid.NewGuid();
        var handler = CreateAppointmentsHandler(new Mock<IReadRepository<Appointment>>(), reader, appointmentsEnabled: true);

        var result = await handler.Handle(new GetAppointmentsListQuery(Page, ClientId: clientId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured!.ClientId.Should().Be(clientId);
        _pets.Verify(r => r.ListAsync(It.IsAny<ISpecification<Pet>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private GetAppointmentsListQueryHandler CreateAppointmentsHandler(
        Mock<IReadRepository<Appointment>> appointments,
        Mock<IAppointmentReadModelReader> reader,
        bool appointmentsEnabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            appointments.Object,
            _pets.Object,
            _clients.Object,
            new Mock<IReadRepository<Clinic>>().Object,
            reader.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions { AppointmentsEnabled = appointmentsEnabled }));
}
