using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetAll;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Permissions.Queries;

public sealed class GetAllPermissionsQueryHandlerTests
{
    private readonly Mock<IPermissionRepository> _repo = new();

    private GetAllPermissionsQueryHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnEmptyPage_When_NoPermissions()
    {
        // Arrange
        var handler = CreateHandler();
        var query = new GetAllPermissionsQuery(new PageRequest { Page = 1, PageSize = 10 });

        _repo.Setup(r => r.GetListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PermissionDto>());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Should_PageAndReturnPermissions_When_Exist()
    {
        // Arrange
        var handler = CreateHandler();
        var all = new List<PermissionDto>
        {
            new(Guid.NewGuid(), "P1", "d1"),
            new(Guid.NewGuid(), "P2", "d2"),
            new(Guid.NewGuid(), "P3", "d3")
        };

        _repo.Setup(r => r.GetListAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(all);

        var query = new GetAllPermissionsQuery(new PageRequest { Page = 1, PageSize = 2 });

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalItems.Should().Be(3);
        result.Items.Should().HaveCount(2);
        result.Items.Select(x => x.Code).Should().Contain(new[] { "P1", "P2" });
    }
}

