using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Queries.GetList;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Appointments.Handlers;

/// <summary>CQRS-12D-5: SharedSearchLookupEnabled routing for appointment list Command DB path.</summary>
public sealed class GetAppointmentsListQueryHandlerSharedSearchLookupFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Appointment>> _appointments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IAppointmentReadModelReader> _readModelReader = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandDbPath_SearchLookup_Should_RouteBySharedSearchFlag(bool sharedSearchLookupEnabled)
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupCommandDbAggregateMocks();
        SetupSharedSearchMocks(sharedSearchLookupEnabled);

        await CreateHandler(
                appointmentsEnabled: false,
                sharedSearchLookupEnabled: sharedSearchLookupEnabled)
            .Handle(
                new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
                CancellationToken.None);

        AssertSharedSearchRouting(sharedSearchLookupEnabled);
    }

    [Fact]
    public async Task QueryReadModelPath_Should_NotUseSharedSearchLookup_EvenWhenFlagTrue()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentListReadResult(Array.Empty<AppointmentListItemDto>(), 0));

        await CreateHandler(
                appointmentsEnabled: true,
                sharedSearchLookupEnabled: true)
            .Handle(
                new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
                CancellationToken.None);

        _readModelReader.Verify(
            r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _appointments.Verify(
            r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task QueryReadModelPath_Should_NotUseCommandDbSearchSpecs_WhenSharedSearchFlagFalse()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentListReadResult(Array.Empty<AppointmentListItemDto>(), 0));

        await CreateHandler(
                appointmentsEnabled: true,
                sharedSearchLookupEnabled: false)
            .Handle(
                new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
                CancellationToken.None);

        _readModelReader.Verify(
            r => r.GetListAsync(It.IsAny<AppointmentListReadRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _petLookupReader.Verify(
            r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CommandDbPath_Should_NotCallLookup_When_SearchWhitespaceOnly()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupCommandDbAggregateMocks();

        await CreateHandler(
                appointmentsEnabled: false,
                sharedSearchLookupEnabled: true)
            .Handle(
                new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "   " }),
                CancellationToken.None);

        _petLookupReader.Verify(
            r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CommandDbPath_WhenSharedSearchFlagTrue_Should_PassTenantAndEscapedPatternToReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        SetupCommandDbAggregateMocks();
        PetTextSearchLookupRequest? captured = null;
        _petLookupReader.Setup(r => r.ResolvePetIdsByTextSearchAsync(
                It.IsAny<PetTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PetTextSearchLookupRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PetTextSearchLookupResult([]));

        await CreateHandler(
                appointmentsEnabled: false,
                sharedSearchLookupEnabled: true)
            .Handle(
                new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "100%" }),
                CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.SearchContainsLikePattern.Should().Be(
            ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("100%")!));
    }

    [Fact]
    public async Task CommandDbPath_WhenSharedSearchFlagTrue_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _petLookupReader.Setup(r => r.ResolvePetIdsByTextSearchAsync(
                It.IsAny<PetTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(
                appointmentsEnabled: false,
                sharedSearchLookupEnabled: true)
            .Handle(
                new GetAppointmentsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
                CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupCommandDbAggregateMocks()
    {
        _appointments.Setup(r => r.CountAsync(It.IsAny<AppointmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _appointments.Setup(r => r.ListAsync(It.IsAny<AppointmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
    }

    private void SetupSharedSearchMocks(bool sharedSearchLookupEnabled)
    {
        if (sharedSearchLookupEnabled)
        {
            _petLookupReader.Setup(r => r.ResolvePetIdsByTextSearchAsync(
                    It.IsAny<PetTextSearchLookupRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PetTextSearchLookupResult([]));
        }
        else
        {
            _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Client>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
            _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Pet>());
        }
    }

    private void AssertSharedSearchRouting(bool sharedSearchLookupEnabled)
    {
        if (sharedSearchLookupEnabled)
        {
            _petLookupReader.Verify(
                r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _clients.Verify(
                r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        else
        {
            _petLookupReader.Verify(
                r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _pets.Verify(
                r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _clients.Verify(
                r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    private GetAppointmentsListQueryHandler CreateHandler(
        bool appointmentsEnabled,
        bool sharedSearchLookupEnabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _appointments.Object,
            _pets.Object,
            _clients.Object,
            _clinics.Object,
            _readModelReader.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                AppointmentsEnabled = appointmentsEnabled,
                SharedSearchLookupEnabled = sharedSearchLookupEnabled
            }));
}
