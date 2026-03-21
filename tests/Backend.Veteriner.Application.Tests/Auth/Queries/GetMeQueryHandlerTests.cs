using Backend.Veteriner.Application.Auth.Queries.Me;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Queries;

public sealed class GetMeQueryHandlerTests
{
    private readonly Mock<IUserReadRepository> _users = new();

    private GetMeQueryHandler CreateHandler()
        => new(_users.Object);

    [Fact]
    public async Task Handle_Should_ReturnDto_When_UserExists()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var cmd = new GetMeQuery(userId);

        var user = new Backend.Veteriner.Domain.Users.User("user@example.com", "hash");

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.UserId.Should().NotBe(Guid.Empty);
        result.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Handle_Should_ThrowUnauthorized_When_UserNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var cmd = new GetMeQuery(userId);

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Backend.Veteriner.Domain.Users.User?)null);

        // Act
        var act = async () => await handler.Handle(cmd, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}

