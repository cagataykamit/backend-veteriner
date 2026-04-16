using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SpeciesReference.Queries.GetById;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class GetSpeciesByIdQueryHandlerTests
{
    private readonly Mock<IReadRepository<Species>> _read = new();

    private GetSpeciesByIdQueryHandler CreateHandler() => new(_read.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_NotFound()
    {
        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Species?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSpeciesByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Species.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_Found_IncludingInactive()
    {
        var id = Guid.NewGuid();
        var entity = new Species("DOG", "Köpek", 2);
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(entity, id);
        entity.Update("DOG", "Köpek", 2, isActive: false);

        _read.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSpeciesByIdQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(id);
        result.Value.Code.Should().Be("DOG");
        result.Value.Name.Should().Be("Köpek");
        result.Value.DisplayOrder.Should().Be(2);
        result.Value.IsActive.Should().BeFalse();
    }
}
