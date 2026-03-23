using Backend.Veteriner.Application.BreedsReference.Commands.Create;
using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class CreateBreedCommandHandlerTests
{
    private readonly Mock<IReadRepository<Species>> _speciesRead = new();
    private readonly Mock<IReadRepository<Breed>> _breedsRead = new();
    private readonly Mock<IRepository<Breed>> _breedsWrite = new();

    private CreateBreedCommandHandler CreateHandler()
        => new(_speciesRead.Object, _breedsRead.Object, _breedsWrite.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SpeciesMissing()
    {
        var sid = Guid.NewGuid();
        var handler = CreateHandler();
        var command = new CreateBreedCommand(sid, "Golden");

        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Breeds.SpeciesNotFound");
        _breedsWrite.Verify(r => r.AddAsync(It.IsAny<Breed>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_UnderSameSpecies()
    {
        var sid = Guid.NewGuid();
        var handler = CreateHandler();
        var command = new CreateBreedCommand(sid, "Labrador");

        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("DOG", "Köpek"));
        _breedsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedBySpeciesAndNameLowerSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Breed(sid, "labrador"));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Breeds.DuplicateName");
        _breedsWrite.Verify(r => r.AddAsync(It.IsAny<Breed>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Create_When_Valid()
    {
        var sid = Guid.NewGuid();
        var handler = CreateHandler();
        var command = new CreateBreedCommand(sid, "Golden Retriever");

        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("DOG", "Köpek"));
        _breedsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedBySpeciesAndNameLowerSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Breed?)null);

        Breed? captured = null;
        _breedsWrite.Setup(r => r.AddAsync(It.IsAny<Breed>(), It.IsAny<CancellationToken>()))
            .Callback<Breed, CancellationToken>((b, _) => captured = b);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.SpeciesId.Should().Be(sid);
        captured.Name.Should().Be("Golden Retriever");
        _breedsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
