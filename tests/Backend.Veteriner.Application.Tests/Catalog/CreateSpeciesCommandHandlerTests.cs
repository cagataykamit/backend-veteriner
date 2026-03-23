using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Commands.Create;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class CreateSpeciesCommandHandlerTests
{
    private readonly Mock<IReadRepository<Species>> _read = new();
    private readonly Mock<IRepository<Species>> _write = new();

    private CreateSpeciesCommandHandler CreateHandler() => new(_read.Object, _write.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateCode()
    {
        var handler = CreateHandler();
        var command = new CreateSpeciesCommand("DOG", "Köpek", 0);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("DOG", "Mevcut"));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.DuplicateCode");
        _write.Verify(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateName_CaseInsensitive()
    {
        var handler = CreateHandler();
        var command = new CreateSpeciesCommand("NEW", "Köpek", 0);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByNameLowerSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("DOG", "köpek"));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.DuplicateName");
        _write.Verify(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Create_When_Unique()
    {
        var handler = CreateHandler();
        var command = new CreateSpeciesCommand("DOG", "Köpek", 5);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByCodeSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByNameLowerSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);

        Species? captured = null;
        _write.Setup(r => r.AddAsync(It.IsAny<Species>(), It.IsAny<CancellationToken>()))
            .Callback<Species, CancellationToken>((s, _) => captured = s);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Code.Should().Be("DOG");
        captured.Name.Should().Be("Köpek");
        captured.DisplayOrder.Should().Be(5);
        _write.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
