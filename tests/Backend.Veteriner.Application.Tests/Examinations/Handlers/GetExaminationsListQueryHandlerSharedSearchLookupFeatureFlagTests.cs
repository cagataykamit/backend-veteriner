using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Examinations.Queries.GetList;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests.TestHelpers;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Examinations.Handlers;

public sealed class GetExaminationsListQueryHandlerSharedSearchLookupFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Examination>> _examinations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IPetReadModelLookupReader> _petLookupReader = new();
    private readonly Mock<IClinicReadScopeResolver> _scopeResolver = ClinicReadScopeResolverMock.Default();

    [Fact]
    public async Task SearchLookup_WhenFlagFalse_Should_UseCommandDb_NotQueryLookupReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _clients.Setup(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Client>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());

        await CreateHandler(sharedSearchLookupEnabled: false).Handle(
            new GetExaminationsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

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

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_Should_UseQueryLookupReader_NotCommandSearchSpecs()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _petLookupReader.Setup(r => r.ResolvePetIdsByTextSearchAsync(
                It.IsAny<PetTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetTextSearchLookupResult([]));
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());

        await CreateHandler(sharedSearchLookupEnabled: true).Handle(
            new GetExaminationsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

        _petLookupReader.Verify(
            r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantTextFieldsSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(
            r => r.ListAsync(It.IsAny<PetsByTenantForClientIdsSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_Should_PassTenantAndEscapedPatternToReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        PetTextSearchLookupRequest? captured = null;
        _petLookupReader.Setup(r => r.ResolvePetIdsByTextSearchAsync(
                It.IsAny<PetTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<PetTextSearchLookupRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PetTextSearchLookupResult([]));
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());

        await CreateHandler(sharedSearchLookupEnabled: true).Handle(
            new GetExaminationsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "100%" }),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.SearchContainsLikePattern.Should().Be(
            ListQueryTextSearch.BuildContainsLikePattern(ListQueryTextSearch.Normalize("100%")!));
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_AndReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _petLookupReader.Setup(r => r.ResolvePetIdsByTextSearchAsync(
                It.IsAny<PetTextSearchLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db down"));

        var act = () => CreateHandler(sharedSearchLookupEnabled: true).Handle(
            new GetExaminationsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "pamuk" }),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchLookup_WhenFlagTrue_AndSearchWhitespaceOnly_Should_NotCallLookupReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(Guid.NewGuid());
        _examinations.Setup(r => r.CountAsync(It.IsAny<ExaminationsFilteredCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _examinations.Setup(r => r.ListAsync(It.IsAny<ExaminationsFilteredPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExaminationListRow>());

        await CreateHandler(sharedSearchLookupEnabled: true).Handle(
            new GetExaminationsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "   " }),
            CancellationToken.None);

        _petLookupReader.Verify(
            r => r.ResolvePetIdsByTextSearchAsync(It.IsAny<PetTextSearchLookupRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private GetExaminationsListQueryHandler CreateHandler(bool sharedSearchLookupEnabled)
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _scopeResolver.Object,
            _examinations.Object,
            _pets.Object,
            _clients.Object,
            _petLookupReader.Object,
            Options.Create(new QueryReadModelsOptions
            {
                SharedSearchLookupEnabled = sharedSearchLookupEnabled
            }));
}
