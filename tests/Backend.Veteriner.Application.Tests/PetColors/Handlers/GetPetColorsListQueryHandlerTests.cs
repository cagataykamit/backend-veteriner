using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.PetColors.Queries.GetList;
using Backend.Veteriner.Application.PetColors.Specs;
using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.PetColors.Handlers;

public sealed class GetPetColorsListQueryHandlerTests
{
    private readonly Mock<IReadRepository<PetColor>> _read = new();

    private GetPetColorsListQueryHandler CreateHandler() => new(_read.Object);

    [Fact]
    public async Task Handle_Should_ReturnActiveColors_AsDtos()
    {
        var a = new PetColor("BLK", "Siyah", 1);
        var b = new PetColor("WHT", "Beyaz", 2);
        _read.Setup(r => r.ListAsync(It.IsAny<PetColorsActiveOrderedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetColor> { a, b });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetPetColorsListQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].Code.Should().Be("BLK");
        result.Value[0].Name.Should().Be("Siyah");
        result.Value[0].IsActive.Should().BeTrue();
        result.Value[1].Code.Should().Be("WHT");
    }

    [Fact]
    public async Task Handle_Should_ReturnEmpty_When_NoRows()
    {
        _read.Setup(r => r.ListAsync(It.IsAny<PetColorsActiveOrderedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PetColor>());

        var handler = CreateHandler();
        var result = await handler.Handle(new GetPetColorsListQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
