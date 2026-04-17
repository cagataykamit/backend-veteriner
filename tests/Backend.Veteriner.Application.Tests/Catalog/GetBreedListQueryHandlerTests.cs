using Backend.Veteriner.Application.BreedsReference.Queries.GetList;
using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class GetBreedListQueryHandlerTests
{
    private readonly Mock<IReadRepository<Breed>> _read = new();

    private GetBreedListQueryHandler CreateHandler() => new(_read.Object);

    [Fact]
    public async Task Handle_Should_ReturnPaged_When_DataExists()
    {
        var sid = Guid.NewGuid();
        var species = new Species("DOG", "Köpek");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, sid);
        var b = new Breed(sid, "Golden");
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(b, species);

        _read.Setup(r => r.CountAsync(It.IsAny<BreedsCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(It.IsAny<BreedsPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Breed> { b });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetBreedListQuery(new PageRequest { Page = 1, PageSize = 20 }, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        result.Value.Items[0].SpeciesName.Should().Be("Köpek");
        result.Value.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Should_PassNullFilters_When_IsActiveAndSpeciesIdNotSpecified()
    {
        var sid = Guid.NewGuid();
        var species = new Species("DOG", "Köpek");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, sid);
        var b = new Breed(sid, "Golden");
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(b, species);

        _read.Setup(r => r.CountAsync(
                It.Is<BreedsCountSpec>(s => s.IsActiveFilter == null && s.SpeciesIdFilter == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(
                It.Is<BreedsPagedSpec>(s => s.IsActiveFilter == null && s.SpeciesIdFilter == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Breed> { b });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetBreedListQuery(new PageRequest { Page = 1, PageSize = 20 }, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_PassTrueIsActiveFilter_When_IsActiveTrue()
    {
        var sid = Guid.NewGuid();
        var species = new Species("DOG", "Köpek");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, sid);
        var b = new Breed(sid, "Golden");
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(b, species);

        _read.Setup(r => r.CountAsync(
                It.Is<BreedsCountSpec>(s => s.IsActiveFilter == true && s.SpeciesIdFilter == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(
                It.Is<BreedsPagedSpec>(s => s.IsActiveFilter == true && s.SpeciesIdFilter == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Breed> { b });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetBreedListQuery(new PageRequest { Page = 1, PageSize = 20 }, true, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_PassFalseIsActiveFilter_When_IsActiveFalse()
    {
        var sid = Guid.NewGuid();
        var species = new Species("DOG", "Köpek");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, sid);
        var b = new Breed(sid, "Eski");
        b.Update("Eski", false);
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(b, species);

        _read.Setup(r => r.CountAsync(
                It.Is<BreedsCountSpec>(s => s.IsActiveFilter == false && s.SpeciesIdFilter == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(
                It.Is<BreedsPagedSpec>(s => s.IsActiveFilter == false && s.SpeciesIdFilter == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Breed> { b });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetBreedListQuery(new PageRequest { Page = 1, PageSize = 20 }, false, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_PassSpeciesIdFilter_When_SpeciesIdSet()
    {
        var filterSpeciesId = Guid.NewGuid();
        var species = new Species("DOG", "Köpek");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, filterSpeciesId);
        var b = new Breed(filterSpeciesId, "Golden");
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(b, species);

        _read.Setup(r => r.CountAsync(
                It.Is<BreedsCountSpec>(s => s.IsActiveFilter == null && s.SpeciesIdFilter == filterSpeciesId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(
                It.Is<BreedsPagedSpec>(s => s.IsActiveFilter == null && s.SpeciesIdFilter == filterSpeciesId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Breed> { b });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetBreedListQuery(new PageRequest { Page = 1, PageSize = 20 }, null, filterSpeciesId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items[0].SpeciesId.Should().Be(filterSpeciesId);
    }

    [Fact]
    public async Task Handle_Should_PassCombinedFilters_When_IsActiveTrue_And_SpeciesIdSet()
    {
        var filterSpeciesId = Guid.NewGuid();
        var species = new Species("DOG", "Köpek");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, filterSpeciesId);
        var b = new Breed(filterSpeciesId, "Golden");
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(b, species);

        _read.Setup(r => r.CountAsync(
                It.Is<BreedsCountSpec>(s => s.IsActiveFilter == true && s.SpeciesIdFilter == filterSpeciesId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(
                It.Is<BreedsPagedSpec>(s => s.IsActiveFilter == true && s.SpeciesIdFilter == filterSpeciesId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Breed> { b });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetBreedListQuery(new PageRequest { Page = 1, PageSize = 20 }, true, filterSpeciesId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
