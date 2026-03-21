using Backend.Veteriner.Application.Auth.Commands.Sessions.Revoke;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

public sealed class RevokeSessionCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _repo = new();

    private RevokeSessionCommandHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SessionNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RevokeSessionCommand(Guid.NewGuid(), Guid.NewGuid());

        _repo.Setup(r => r.GetByIdAsync(cmd.RefreshTokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.NotFound");
        _repo.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SessionBelongsToAnotherUser()
    {
        // Arrange
        var handler = CreateHandler();
        var cmdUserId = Guid.NewGuid();
        var tokenUserId = Guid.NewGuid();
        var cmd = new RevokeSessionCommand(cmdUserId, Guid.NewGuid());

        var token = new RefreshToken(tokenUserId, "hash", DateTime.UtcNow.AddDays(1), null, null);

        _repo.Setup(r => r.GetByIdAsync(cmd.RefreshTokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.Forbidden");
        _repo.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Revoke_When_SessionIsActive()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var cmd = new RevokeSessionCommand(userId, Guid.NewGuid());

        var token = new RefreshToken(userId, "hash", DateTime.UtcNow.AddDays(1), null, null);

        _repo.Setup(r => r.GetByIdAsync(cmd.RefreshTokenId, It.IsAny<CancellationToken>()))
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
        var cmd = new RevokeSessionCommand(userId, Guid.NewGuid());

        var token = new RefreshToken(userId, "hash", DateTime.UtcNow.AddDays(1), null, null);
        token.Revoke();

        _repo.Setup(r => r.GetByIdAsync(cmd.RefreshTokenId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _repo.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

