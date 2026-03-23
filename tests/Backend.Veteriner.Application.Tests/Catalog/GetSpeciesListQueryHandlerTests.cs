using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.SpeciesReference.Queries.GetList;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Catalog;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Catalog;

public sealed class GetSpeciesListQueryHandlerTests
{
    private readonly Mock<IReadRepository<Species>> _read = new();

    private GetSpeciesListQueryHandler CreateHandler() => new(_read.Object);

    [Fact]
    public async Task Handle_Should_ReturnPaged_When_DataExists()
    {
        var s = new Species("DOG", "Köpek", 1);
        _read.Setup(r => r.CountAsync(It.IsAny<SpeciesCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(It.IsAny<SpeciesPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Species> { s });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSpeciesListQuery(new PageRequest { Page = 1, PageSize = 20 }, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(1);
        result.Value.Items[0].Code.Should().Be("DOG");
        result.Value.TotalItems.Should().Be(1);
    }
}
