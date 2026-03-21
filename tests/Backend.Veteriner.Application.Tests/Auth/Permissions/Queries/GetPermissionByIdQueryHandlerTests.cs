using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetById;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Permissions.Queries;

public sealed class GetPermissionByIdQueryHandlerTests
{
    private readonly Mock<IPermissionRepository> _repo = new();

    private GetPermissionByIdQueryHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var id = Guid.NewGuid();
        var query = new GetPermissionByIdQuery(id);

        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Permissions.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDto_When_PermissionExists()
    {
        // Arrange
        var handler = CreateHandler();
        var id = Guid.NewGuid();
        var query = new GetPermissionByIdQuery(id);

        var perm = new Permission("CODE", "desc");
        typeof(Permission).GetProperty("Id")!.SetValue(perm, id);

        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(perm);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new PermissionDto(id, "CODE", "desc"));
    }
}

