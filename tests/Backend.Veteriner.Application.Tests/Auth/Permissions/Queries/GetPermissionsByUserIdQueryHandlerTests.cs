using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Auth.Queries.Permissions.GetByUserId;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Permissions.Queries;

public sealed class GetPermissionsByUserIdQueryHandlerTests
{
    private readonly Mock<IPermissionReader> _reader = new();

    private GetPermissionsByUserIdQueryHandler CreateHandler()
        => new(_reader.Object);

    [Fact]
    public async Task Handle_Should_ReturnPermissions_FromReader()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new GetPermissionsByUserIdQuery(userId);

        var perms = new List<string> { "p1", "p2" }.AsReadOnly();

        _reader.Setup(r => r.GetPermissionsAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(perms);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(perms);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyList_When_ReaderReturnsEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new GetPermissionsByUserIdQuery(userId);

        _reader.Setup(r => r.GetPermissionsAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>().AsReadOnly());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}

