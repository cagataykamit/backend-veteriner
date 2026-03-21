using Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeAllMy;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

public sealed class RevokeAllMySessionsCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private readonly Mock<IClientContext> _client = new();

    private RevokeAllMySessionsCommandHandler CreateHandler()
        => new(_repo.Object, _client.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserIsUnauthenticated()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RevokeAllMySessionsCommand(Guid.NewGuid());

        _client.SetupGet(c => c.UserId).Returns((Guid?)null);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.Unauthorized");
        _repo.Verify(r => r.RevokeAllByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_RequestedUserId_DoesNotMatch_CurrentUser()
    {
        // Arrange
        var handler = CreateHandler();
        var currentUserId = Guid.NewGuid();
        var cmd = new RevokeAllMySessionsCommand(Guid.NewGuid());

        _client.SetupGet(c => c.UserId).Returns(currentUserId);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.Forbidden");
        _repo.Verify(r => r.RevokeAllByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_RevokeAllSessions_When_UserIdsMatch()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var cmd = new RevokeAllMySessionsCommand(userId);

        _client.SetupGet(c => c.UserId).Returns(userId);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.RevokeAllByUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}

