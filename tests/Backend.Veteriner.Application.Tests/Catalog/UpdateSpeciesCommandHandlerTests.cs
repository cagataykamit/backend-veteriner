using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Commands.Update;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class UpdateSpeciesCommandHandlerTests
{
    private readonly Mock<IReadRepository<Species>> _read = new();
    private readonly Mock<IRepository<Species>> _write = new();

    private UpdateSpeciesCommandHandler CreateHandler() => new(_read.Object, _write.Object);

    private static Species CreateSpeciesWithId(Guid id, string code, string name, int displayOrder, bool isActive = true)
    {
        var s = new Species(code, name, displayOrder);
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(s, id);
        if (!isActive)
            s.Update(code, name, displayOrder, isActive: false);
        return s;
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_NotFound()
    {
        var id = Guid.NewGuid();
        var handler = CreateHandler();
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);

        var result = await handler.Handle(
            new UpdateSpeciesCommand(id, "DOG", "Köpek", 0, true),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.NotFound");
        _write.Verify(r => r.UpdateAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateCode()
    {
        var id = Guid.NewGuid();
        var existing = CreateSpeciesWithId(id, "DOG", "Köpek", 0);
        var other = CreateSpeciesWithId(Guid.NewGuid(), "CAT", "Kedi", 0);

        var handler = CreateHandler();
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var result = await handler.Handle(
            new UpdateSpeciesCommand(id, "CAT", "Köpek", 0, true),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.DuplicateCode");
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_CaseInsensitive()
    {
        var id = Guid.NewGuid();
        var existing = CreateSpeciesWithId(id, "DOG", "Köpek", 0);
        var other = CreateSpeciesWithId(Guid.NewGuid(), "CAT", "kedi", 0);

        var handler = CreateHandler();
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByNameLowerExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(other);

        var result = await handler.Handle(
            new UpdateSpeciesCommand(id, "DOG", "Kedi", 0, true),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.DuplicateName");
    }

    [Fact]
    public async Task Handle_Should_Update_When_Unique()
    {
        var id = Guid.NewGuid();
        var existing = CreateSpeciesWithId(id, "DOG", "Köpek", 1);

        var handler = CreateHandler();
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByNameLowerExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);

        Species? updated = null;
        _write.Setup(r => r.UpdateAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()))
            .Callback<Species, CancellationToken>((s, _) => updated = s);

        var result = await handler.Handle(
            new UpdateSpeciesCommand(id, "DOG2", "Tazı", 3, false),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.Code.Should().Be("DOG2");
        updated.Name.Should().Be("Tazı");
        updated.DisplayOrder.Should().Be(3);
        updated.IsActive.Should().BeFalse();
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
