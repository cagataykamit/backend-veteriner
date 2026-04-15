using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Queries.GetList;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class GetPetsListQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetPetsListQueryHandler CreateHandler()
        => new(_tenantContext.Object, _pets.Object, _clients.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);
        var query = new GetPetsListQuery(new PageRequest { Page = 1, PageSize = 20 });

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _pets.Verify(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _clients.Verify(r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyPage_When_NoRows()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _pets.Setup(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var query = new GetPetsListQuery(new PageRequest { Page = 1, PageSize = 20 });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var p = result.Value!;
        p.Items.Should().BeEmpty();
        p.TotalItems.Should().Be(0);
        p.Page.Should().Be(1);
        p.PageSize.Should().Be(20);
        p.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Should_MapListItems_And_UseZero_When_WeightNull()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var species = new Species("CAT", "Kedi");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, TestSpeciesIds.Cat);

        var petNoWeight = new Pet(tid, cid, "A", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(petNoWeight, Guid.NewGuid());
        typeof(Pet).GetProperty(nameof(Pet.Species))!.SetValue(petNoWeight, species);

        var petWeighted = new Pet(tid, cid, "B", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(petWeighted, Guid.NewGuid());
        typeof(Pet).GetProperty(nameof(Pet.Species))!.SetValue(petWeighted, species);
        petWeighted.UpdateDetails("B", TestSpeciesIds.Cat, null, null, null, null, null, 3.25m, null);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _pets.Setup(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet> { petNoWeight, petWeighted });

        var result = await CreateHandler().Handle(
            new GetPetsListQuery(new PageRequest { Page = 1, PageSize = 20 }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value!.Items;
        items.Should().HaveCount(2);
        items[0].Weight.Should().Be(0);
        items[1].Weight.Should().Be(3.25m);
        items[0].SpeciesName.Should().Be("Kedi");
    }

    [Fact]
    public async Task Handle_Should_ClampPageAndPageSize()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _pets.Setup(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var query = new GetPetsListQuery(new PageRequest { Page = 0, PageSize = 500 });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task Handle_Should_NotQueryClientsByText_When_SearchWhitespaceOnly()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _pets.Setup(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var query = new GetPetsListQuery(new PageRequest { Page = 1, PageSize = 20, Search = "   " });
        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _clients.Verify(
            r => r.ListAsync(It.IsAny<ClientsByTenantTextSearchSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _pets.Verify(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_PassClientAndSpeciesFilters_ToSpecs()
    {
        var tid = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var speciesId = TestSpeciesIds.Dog;
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _pets.Setup(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _pets.Setup(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Pet>());

        var result = await CreateHandler().Handle(
            new GetPetsListQuery(new PageRequest { Page = 1, PageSize = 10 }, clientId, speciesId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _pets.Verify(r => r.CountAsync(It.IsAny<PetsByTenantCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
        _pets.Verify(r => r.ListAsync(It.IsAny<PetsByTenantPagedSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
