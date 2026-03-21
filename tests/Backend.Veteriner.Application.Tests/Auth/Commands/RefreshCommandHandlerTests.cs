using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Auth.Commands.Refresh;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

public sealed class RefreshCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
    private readonly Mock<ITokenHashService> _hash = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IJwtOptionsProvider> _opt = new();
    private readonly Mock<IOperationClaimPermissionRepository> _ocpRepo = new();

    private RefreshCommandHandler CreateHandler()
        => new(_refreshRepo.Object, _hash.Object, _jwt.Object, _opt.Object, _ocpRepo.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_RefreshToken_IsEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RefreshCommand(string.Empty);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.InvalidRefreshToken");
        _refreshRepo.Verify(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_StoredToken_NotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");
        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.RefreshTokenNotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TokenIsReused()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var user = new User("user@example.com", "hash");
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null);
        stored.Revoke("reused");
        typeof(RefreshToken).GetProperty("User")!.SetValue(stored, user);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.RefreshTokenReused");
        _refreshRepo.Verify(r => r.RevokeAllByUserAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TokenIsExpired()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var user = new User("user@example.com", "hash");
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(-1), null, null);
        typeof(RefreshToken).GetProperty("User")!.SetValue(stored, user);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.RefreshTokenReused");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserIsNull()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var stored = new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(1), null, null);
        // User navigation null bırakılıyor

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserNotFound");
    }

    [Fact]
    public async Task Handle_Should_IssueNewTokens_When_RefreshIsValid()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var user = new User("user@example.com", "hash");
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null);
        typeof(RefreshToken).GetProperty("User")!.SetValue(stored, user);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        _ocpRepo.Setup(r => r.GetPermissionCodesByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "p1" });

        _opt.SetupGet(o => o.RefreshTokenDays).Returns(7);

        _jwt.Setup(j => j.Create(user, It.IsAny<IReadOnlyCollection<System.Security.Claims.Claim>>()))
            .Returns(("access-new", "raw-new", DateTime.UtcNow.AddMinutes(5)));

        _hash.Setup(h => h.ComputeSha256("raw-new")).Returns("hash-new");

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Pattern ile hem IsSuccess hem Value != null akışını netleştir.
        if (result is not { IsSuccess: true, Value: { } value })
            return;

        value.AccessToken.Should().Be("access-new");
        value.RefreshToken.Should().Be("raw-new");

        _refreshRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

