using Backend.Veteriner.Application.BreedsReference.Commands.Update;
using Backend.Veteriner.Application.BreedsReference.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class UpdateBreedCommandHandlerTests
{
    private readonly Mock<IReadRepository<Breed>> _read = new();
    private readonly Mock<IRepository<Breed>> _write = new();

    private UpdateBreedCommandHandler CreateHandler() => new(_read.Object, _write.Object);

    private static Breed CreateBreedWithId(Guid id, Guid speciesId, string name)
    {
        var b = new Breed(speciesId, name);
        typeof(Breed).GetProperty(nameof(Breed.Id))!.SetValue(b, id);
        return b;
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_NotFound()
    {
        var id = Guid.NewGuid();
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedByIdWithSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Breed?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new UpdateBreedCommand(id, "Yeni Ad", true), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Breeds.NotFound");
        _write.Verify(r => r.UpdateAsync(It.IsAny<Breed>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_UnderSameSpecies()
    {
        var speciesId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var existing = CreateBreedWithId(id, speciesId, "Golden");
        var other = CreateBreedWithId(Guid.NewGuid(), speciesId, "golden retriever");

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedByIdWithSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedBySpeciesAndNameLowerExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var handler = CreateHandler();
        var result = await handler.Handle(new UpdateBreedCommand(id, "Golden Retriever", true), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Breeds.DuplicateName");
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Update_When_Unique()
    {
        var speciesId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var existing = CreateBreedWithId(id, speciesId, "Golden");

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedByIdWithSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<BreedBySpeciesAndNameLowerExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Breed?)null);

        Breed? updated = null;
        _write.Setup(r => r.UpdateAsync(It.IsAny<Breed>(), It.IsAny<CancellationToken>()))
            .Callback<Breed, CancellationToken>((b, _) => updated = b);

        var handler = CreateHandler();
        var result = await handler.Handle(new UpdateBreedCommand(id, "Golden Retriever", false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Golden Retriever");
        updated.IsActive.Should().BeFalse();
        updated.SpeciesId.Should().Be(speciesId);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
