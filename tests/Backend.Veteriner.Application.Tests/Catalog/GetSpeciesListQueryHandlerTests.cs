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

    [Fact]
    public async Task Handle_Should_PassNullIsActiveFilter_When_IsActiveNotSpecified()
    {
        var s = new Species("DOG", "Köpek", 1);
        _read.Setup(r => r.CountAsync(It.Is<SpeciesCountSpec>(spec => spec.IsActiveFilter == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(It.Is<SpeciesPagedSpec>(spec => spec.IsActiveFilter == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Species> { s });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSpeciesListQuery(new PageRequest { Page = 1, PageSize = 20 }, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Should_PassTrueIsActiveFilter_When_IsActiveTrue()
    {
        var s = new Species("DOG", "Köpek", 1);
        _read.Setup(r => r.CountAsync(It.Is<SpeciesCountSpec>(spec => spec.IsActiveFilter == true), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(It.Is<SpeciesPagedSpec>(spec => spec.IsActiveFilter == true), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Species> { s });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSpeciesListQuery(new PageRequest { Page = 1, PageSize = 20 }, true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_PassFalseIsActiveFilter_When_IsActiveFalse()
    {
        var s = new Species("DOG", "Eski", 1);
        s.Update("DOG", "Eski", 1, isActive: false);
        _read.Setup(r => r.CountAsync(It.Is<SpeciesCountSpec>(spec => spec.IsActiveFilter == false), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _read.Setup(r => r.ListAsync(It.Is<SpeciesPagedSpec>(spec => spec.IsActiveFilter == false), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Species> { s });

        var handler = CreateHandler();
        var result = await handler.Handle(new GetSpeciesListQuery(new PageRequest { Page = 1, PageSize = 20 }, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items[0].IsActive.Should().BeFalse();
    }
}
