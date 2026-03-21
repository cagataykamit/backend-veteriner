using System.Collections.Generic;
using System.Security.Claims;
using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

public sealed class LoginCommandHandlerTests
{
    private readonly Mock<IUserReadRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ITokenHashService> _tokenHash = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IJwtOptionsProvider> _jwtOpt = new();
    private readonly Mock<IOperationClaimPermissionRepository> _ocpRepo = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();

    private LoginCommandHandler CreateHandler(SessionOptions? sessionOptions = null)
    {
        var opt = Options.Create(sessionOptions ?? new SessionOptions { SingleSessionPerUser = false });
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        return new LoginCommandHandler(
            _users.Object,
            _hasher.Object,
            _jwt.Object,
            _tokenHash.Object,
            _refreshRepo.Object,
            _client.Object,
            _jwtOpt.Object,
            _ocpRepo.Object,
            _tenants.Object,
            opt);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new LoginCommand("user@example.com", "password");

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.InvalidCredentials");
        _refreshRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PasswordIsInvalid()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new LoginCommand("user@example.com", "wrong");

        var user = new User("user@example.com", "hash");
        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(cmd.Password, user.PasswordHash)).Returns(false);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.InvalidCredentials");
        _refreshRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_LoginSuccessfully_When_CredentialsAreValid()
    {
        // Arrange
        var sessionOptions = new SessionOptions { SingleSessionPerUser = true };
        var handler = CreateHandler(sessionOptions);
        var cmd = new LoginCommand("user@example.com", "password");

        var user = new User("user@example.com", "hash");
        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(cmd.Password, user.PasswordHash)).Returns(true);

        _ocpRepo.Setup(r => r.GetPermissionCodesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "p1", "p2" });

        _jwtOpt.SetupGet(o => o.RefreshTokenDays).Returns(7);

        _jwt.Setup(j => j.Create(user, It.IsAny<IEnumerable<Claim>>()))
            .Returns(("access-token", "refresh-token-raw", DateTime.UtcNow.AddMinutes(5)));

        _tokenHash.Setup(t => t.ComputeSha256("refresh-token-raw")).Returns("refresh-hash");

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // Pattern ile hem IsSuccess hem Value != null akışını netleştir.
        if (result is not { IsSuccess: true, Value: { } value })
            return;

        value.AccessToken.Should().Be("access-token");
        value.RefreshToken.Should().Be("refresh-token-raw");

        _refreshRepo.Verify(r => r.RevokeAllByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_IncludeTenantClaim_When_TenantIdProvidedAndActive()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new LoginCommand("user@example.com", "password", tid);
        var user = new User("user@example.com", "hash");
        var tenant = new Tenant("Acme");

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(cmd.Password, user.PasswordHash)).Returns(true);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _ocpRepo.Setup(r => r.GetPermissionCodesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _jwtOpt.SetupGet(o => o.RefreshTokenDays).Returns(7);

        List<Claim>? captured = null;
        _jwt.Setup(j => j.Create(user, It.IsAny<IEnumerable<Claim>>()))
            .Callback<User, IEnumerable<Claim>?>((_, c) => captured = c?.ToList())
            .Returns(("a", "r", DateTime.UtcNow.AddMinutes(5)));
        _tokenHash.Setup(t => t.ComputeSha256("r")).Returns("h");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Should().Contain(c => c.Type == VeterinerClaims.TenantId && c.Value == tid.ToString("D"));
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantInactive()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new LoginCommand("user@example.com", "password", tid);
        var user = new User("user@example.com", "hash");
        var tenant = new Tenant("Acme");
        tenant.Deactivate();

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(cmd.Password, user.PasswordHash)).Returns(true);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
    }
}

