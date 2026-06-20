using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetList;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.LabResults.Queries.GetList;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Prescriptions.Queries.GetList;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Application.Treatments.Queries.GetList;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>CQRS-12D-4: SharedSearchLookupEnabled routing for Strategy A clinical list handlers.</summary>
public sealed class ClinicalListSharedSearchLookupFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TreatmentsList_SearchLookup_Should_RouteByFlag(bool sharedSearchLookupEnabled)
    {
        var treatments = new Mock<IReadRepository<Treatment>>();
        SetupCommonSearchMocks(sharedSearchLookupEnabled);
        SetupEmptyTreatmentsAggregateQueries(treatments);

        var handler = new GetTreatmentsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            treatments.Object,
            _pets.Object,
            _clients.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions { SharedSearchLookupEnabled = sharedSearchLookupEnabled }));

        await handler.Handle(
            new GetTreatmentsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

        AssertSearchRouting(sharedSearchLookupEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VaccinationsList_SearchLookup_Should_RouteByFlag(bool sharedSearchLookupEnabled)
    {
        var vaccinations = new Mock<IReadRepository<Vaccination>>();
        SetupCommonSearchMocks(sharedSearchLookupEnabled);
        SetupEmptyAggregateQueries(vaccinations);

        var handler = new GetVaccinationsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            vaccinations.Object,
            _pets.Object,
            _clients.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions { SharedSearchLookupEnabled = sharedSearchLookupEnabled }));

        await handler.Handle(
            new GetVaccinationsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

        AssertSearchRouting(sharedSearchLookupEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HospitalizationsList_SearchLookup_Should_RouteByFlag(bool sharedSearchLookupEnabled)
    {
        var hospitalizations = new Mock<IReadRepository<Hospitalization>>();
        SetupCommonSearchMocks(sharedSearchLookupEnabled);
        SetupEmptyAggregateQueries(hospitalizations);

        var handler = new GetHospitalizationsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            hospitalizations.Object,
            _pets.Object,
            _clients.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions { SharedSearchLookupEnabled = sharedSearchLookupEnabled }));

        await handler.Handle(
            new GetHospitalizationsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

        AssertSearchRouting(sharedSearchLookupEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LabResultsList_SearchLookup_Should_RouteByFlag(bool sharedSearchLookupEnabled)
    {
        var labResults = new Mock<IReadRepository<LabResult>>();
        SetupCommonSearchMocks(sharedSearchLookupEnabled);
        SetupEmptyAggregateQueries(labResults);

        var handler = new GetLabResultsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            labResults.Object,
            _pets.Object,
            _clients.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions { SharedSearchLookupEnabled = sharedSearchLookupEnabled }));

        await handler.Handle(
            new GetLabResultsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

        AssertSearchRouting(sharedSearchLookupEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrescriptionsList_SearchLookup_Should_RouteByFlag(bool sharedSearchLookupEnabled)
    {
        var prescriptions = new Mock<IReadRepository<Prescription>>();
        SetupCommonSearchMocks(sharedSearchLookupEnabled);
        SetupEmptyAggregateQueries(prescriptions);

        var handler = new GetPrescriptionsListQueryHandler(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            prescriptions.Object,
            _pets.Object,
            _clients.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions { SharedSearchLookupEnabled = sharedSearchLookupEnabled }));

        await handler.Handle(
            new GetPrescriptionsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

        AssertSearchRouting(sharedSearchLookupEnabled);
    }

    private void SetupCommonSearchMocks(bool sharedSearchLookupEnabled)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Guid.NewGuid());
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());

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

    private static void SetupEmptyAggregateQueries<T>(Mock<IReadRepository<T>> repo) where T : class
    {
        repo.Setup(r => r.CountAsync(It.IsAny<Ardalis.Specification.ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repo.Setup(r => r.ListAsync(It.IsAny<Ardalis.Specification.ISpecification<T>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<T>());
    }

    /// <summary>
    /// Treatments list uses <see cref="TreatmentsFilteredPagedSpec"/> projection (<see cref="TreatmentListRow"/>),
    /// not <c>ISpecification&lt;Treatment&gt;</c> entity list — generic aggregate setup does not match ListAsync.
    /// </summary>
    private static void SetupEmptyTreatmentsAggregateQueries(Mock<IReadRepository<Treatment>> repo)
    {
        repo.Setup(r => r.CountAsync(It.IsAny<TreatmentsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        repo.Setup(r => r.ListAsync(It.IsAny<TreatmentsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TreatmentListRow>());
    }

    private void AssertSearchRouting(bool sharedSearchLookupEnabled)
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
}
