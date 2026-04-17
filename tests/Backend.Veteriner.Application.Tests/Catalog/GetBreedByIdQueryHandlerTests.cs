using Backend.Veteriner.Application.BreedsReference.Queries.GetById;
using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class GetBreedByIdQueryHandlerTests
{
    private readonly Mock<IReadRepository<Breed>> _read = new();

    private GetBreedByIdQueryHandler CreateHandler() => new(_read.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_NotFound()
    {
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedByIdWithSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Breed?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetBreedByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Breeds.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SpeciesNavigationMissing()
    {
        var id = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var breed = new Breed(speciesId, "Golden");
        typeof(Breed).GetProperty(nameof(Breed.Id))!.SetValue(breed, id);
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(breed, null);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedByIdWithSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(breed);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetBreedByIdQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Breeds.Inconsistent");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_With_SpeciesCode()
    {
        var id = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var breed = new Breed(speciesId, "Golden");
        typeof(Breed).GetProperty(nameof(Breed.Id))!.SetValue(breed, id);
        var species = new Species("DOG", "Köpek");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, speciesId);
        typeof(Breed).GetProperty(nameof(Breed.Species))!.SetValue(breed, species);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedByIdWithSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(breed);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetBreedByIdQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SpeciesCode.Should().Be("DOG");
        result.Value.SpeciesName.Should().Be("Köpek");
        result.Value.Name.Should().Be("Golden");
        result.Value.IsActive.Should().BeTrue();
    }
}
