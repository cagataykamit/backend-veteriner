using System.Collections.Generic;
using System.Security.Claims;
using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Application.Auth.Commands.Refresh;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
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
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IUserTenantRepository> _userTenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private RefreshCommandHandler CreateHandler()
    {
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        return new RefreshCommandHandler(
            _refreshRepo.Object,
            _hash.Object,
            _jwt.Object,
            _opt.Object,
            _ocpRepo.Object,
            _tenants.Object,
            _userTenants.Object,
            _clinics.Object);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_RefreshToken_IsEmpty()
    {
        var handler = CreateHandler();
        var cmd = new RefreshCommand(string.Empty);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.InvalidRefreshToken");
        _refreshRepo.Verify(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_StoredToken_NotFound()
    {
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");
        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.RefreshTokenNotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TokenIsReused()
    {
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var user = new User("user@example.com", "hash");
        var tid = Guid.NewGuid();
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null, tid);
        stored.Revoke("reused");
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(stored, user);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.RefreshTokenReused");
        _refreshRepo.Verify(r => r.RevokeAllByUserAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TokenIsExpired()
    {
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var user = new User("user@example.com", "hash");
        var tid = Guid.NewGuid();
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(-1), null, null, tid);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(stored, user);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.RefreshTokenExpired");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserIsNull()
    {
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var tid = Guid.NewGuid();
        var stored = new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(1), null, null, tid);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserNotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_SessionTenantMissing()
    {
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var user = new User("user@example.com", "hash");
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null, null);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(stored, user);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.RefreshSessionRequiresReLogin");
    }

    [Fact]
    public async Task Handle_Should_IssueNewTokens_When_RefreshIsValid()
    {
        var handler = CreateHandler();
        var cmd = new RefreshCommand("raw-token");

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");

        var user = new User("user@example.com", "hash");
        var tid = Guid.NewGuid();
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null, tid);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(stored, user);

        var tenant = new Tenant("Acme");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);

        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        _ocpRepo.Setup(r => r.GetPermissionCodesByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "p1" });

        _opt.SetupGet(o => o.RefreshTokenDays).Returns(7);

        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userTenants.Setup(r => r.ExistsAsync(user.Id, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _jwt.Setup(j => j.Create(user, It.IsAny<IEnumerable<Claim>>()))
            .Returns(("access-new", "raw-new", DateTime.UtcNow.AddMinutes(5)));

        _hash.Setup(h => h.ComputeSha256("raw-new")).Returns("hash-new");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        if (result is not { IsSuccess: true, Value: { } value })
            return;

        value.AccessToken.Should().Be("access-new");
        value.RefreshToken.Should().Be("raw-new");
        value.ResolvedTenantId.Should().Be(tid);
        value.TenantMembershipCount.Should().BeNull();

        _refreshRepo.Verify(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _refreshRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_IncludeTenantClaim_FromStoredSession()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new RefreshCommand("raw-token");
        var user = new User("user@example.com", "hash");
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null, tid);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(stored, user);
        var tenant = new Tenant("Acme");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);

        _hash.Setup(h => h.ComputeSha256(cmd.RefreshToken)).Returns("hash");
        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userTenants.Setup(r => r.ExistsAsync(user.Id, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _ocpRepo.Setup(r => r.GetPermissionCodesByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _opt.SetupGet(o => o.RefreshTokenDays).Returns(7);

        List<Claim>? captured = null;
        _jwt.Setup(j => j.Create(user, It.IsAny<IEnumerable<Claim>>()))
            .Callback<User, IEnumerable<Claim>?>((_, c) => captured = c?.ToList())
            .Returns(("a", "raw-new", DateTime.UtcNow.AddMinutes(5)));
        _hash.Setup(h => h.ComputeSha256("raw-new")).Returns("h-new");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Should().Contain(c => c.Type == VeterinerClaims.TenantId && c.Value == tid.ToString("D"));
    }
}
