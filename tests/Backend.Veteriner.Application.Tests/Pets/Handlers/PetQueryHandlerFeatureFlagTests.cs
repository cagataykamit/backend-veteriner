using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Queries.GetList;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class PetQueryHandlerFeatureFlagTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();
    private readonly Mock<IPetReadModelReader> _readModelReader = new();

    [Fact]
    public async Task List_WhenFlagFalse_Should_UseCommandRepository_NotQueryReader()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _pets.Setup(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetListProjectionRow>());

        var handler = CreateListHandler(false);
        await handler.Handle(new GetPetsListQuery(new PageRequest()), CancellationToken.None);

        _pets.Verify(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_UseQueryReader_NotCommandRepository()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetListReadResult(Array.Empty<PetListItemDto>(), 0));

        var handler = CreateListHandler(true);
        await handler.Handle(new GetPetsListQuery(new PageRequest()), CancellationToken.None);

        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _pets.Verify(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _clients.Verify(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_PassTenantScopeFiltersAndSearchToReader()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var speciesId = TestSpeciesIds.Dog;
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        PetListReadRequest? captured = null;
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PetListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PetListReadResult(Array.Empty<PetListItemDto>(), 0));

        var handler = CreateListHandler(true);
        await handler.Handle(
            new GetPetsListQuery(new PageRequest { Page = 2, PageSize = 25, Search = "pamuk" }, clientId, speciesId),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(tid);
        captured.Page.Should().Be(2);
        captured.PageSize.Should().Be(25);
        captured.ClientId.Should().Be(clientId);
        captured.SpeciesId.Should().Be(speciesId);
        captured.SearchContainsLikePattern.Should().Be("%pamuk%");
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_EscapeLikeWildcardsInSearchPattern()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        PetListReadRequest? captured = null;
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PetListReadRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PetListReadResult(Array.Empty<PetListItemDto>(), 0));

        var handler = CreateListHandler(true);
        await handler.Handle(
            new GetPetsListQuery(new PageRequest { Search = "50%_off" }),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SearchContainsLikePattern.Should().Be("%50[%][_]off%");
    }

    [Fact]
    public async Task List_WhenFlagTrue_AndTenantMissing_Should_ReturnFailure_WithoutHittingReader()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var handler = CreateListHandler(true);
        var result = await handler.Handle(new GetPetsListQuery(new PageRequest()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenQueryReaderThrows_Should_NotFallbackToCommandDb()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("query db unavailable"));

        var handler = CreateListHandler(true);
        var act = () => handler.Handle(new GetPetsListQuery(new PageRequest()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _pets.Verify(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_WhenFlagTrue_Should_FillStatus_From_CommandRepository()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        var deceased = new Pet(tid, Guid.NewGuid(), "Boncuk", TestSpeciesIds.Dog);
        deceased.SetStatus(PetStatus.Deceased, null, new DateOnly(2026, 10, 14));
        var active = new Pet(tid, Guid.NewGuid(), "Pamuk", TestSpeciesIds.Dog);
        _readModelReader.Setup(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PetListReadResult(
                [
                    new PetListItemDto(deceased.Id, tid, deceased.ClientId, "Boncuk", TestSpeciesIds.Dog, "Köpek", null, null, null, 0),
                    new PetListItemDto(active.Id, tid, active.ClientId, "Pamuk", TestSpeciesIds.Dog, "Köpek", null, null, null, 0),
                ],
                2));
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { deceased, active });

        var result = await CreateListHandler(true).Handle(new GetPetsListQuery(new PageRequest()), CancellationToken.None);

        result.Value!.Items.Select(i => i.Status).Should().Equal(PetStatus.Deceased, PetStatus.Active);
    }

    [Fact]
    public async Task List_WhenFlagTrue_And_StatusFilter_Should_UseCommandRepository()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _pets.Setup(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetListProjectionRow>());

        await CreateListHandler(true).Handle(
            new GetPetsListQuery(new PageRequest(), Status: PetStatus.Deceased), CancellationToken.None);

        _readModelReader.Verify(r => r.GetListAsync(It.IsAny<PetListReadRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _pets.Verify(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private GetPetsListQueryHandler CreateListHandler(bool enabled)
        => new(
            _tenantContext.Object,
            _pets.Object,
            _clients.Object,
            _readModelReader.Object,
            Options.Create(new QueryReadModelsOptions { PetsEnabled = enabled }));
}
