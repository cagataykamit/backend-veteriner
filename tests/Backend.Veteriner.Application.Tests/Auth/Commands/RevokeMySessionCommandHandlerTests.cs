using Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeMy;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

public sealed class RevokeMySessionCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private readonly Mock<IClientContext> _client = new();

    private RevokeMySessionCommandHandler CreateHandler()
        => new(_repo.Object, _client.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserIsUnauthenticated()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RevokeMySessionCommand(Guid.NewGuid());

        _client.SetupGet(c => c.UserId).Returns((Guid?)null);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.Unauthorized");
        _repo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SessionNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var cmd = new RevokeMySessionCommand(Guid.NewGuid());

        _client.SetupGet(c => c.UserId).Returns(userId);
        _repo.Setup(r => r.GetByIdAsync(cmd.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SessionBelongsToAnotherUser()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var cmd = new RevokeMySessionCommand(Guid.NewGuid());

        _client.SetupGet(c => c.UserId).Returns(userId);

        var token = new RefreshToken(otherUserId, "hash", DateTime.UtcNow.AddDays(1), null, null);
        _repo.Setup(r => r.GetByIdAsync(cmd.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.Forbidden");
        _repo.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Revoke_When_SessionIsActiveAndOwnedByUser()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var cmd = new RevokeMySessionCommand(Guid.NewGuid());

        _client.SetupGet(c => c.UserId).Returns(userId);

        var token = new RefreshToken(userId, "hash", DateTime.UtcNow.AddDays(1), null, null);
        _repo.Setup(r => r.GetByIdAsync(cmd.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.RevokeAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_BeIdempotent_When_SessionAlreadyRevoked()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var cmd = new RevokeMySessionCommand(Guid.NewGuid());

        _client.SetupGet(c => c.UserId).Returns(userId);

        var token = new RefreshToken(userId, "hash", DateTime.UtcNow.AddDays(1), null, null);
        token.Revoke();
        _repo.Setup(r => r.GetByIdAsync(cmd.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

