using Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Remove;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Claims.Handlers;

public sealed class RemovePermissionFromClaimCommandHandlerTests
{
    private readonly Mock<IOperationClaimPermissionRepository> _repo = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();

    private RemovePermissionFromClaimCommandHandler CreateHandler(bool revokeSessions = false)
    {
        var opt = Options.Create(new PermissionChangeOptions
        {
            RevokeSessionsOnPermissionChange = revokeSessions
        });
        return new RemovePermissionFromClaimCommandHandler(
            _repo.Object,
            _cache.Object,
            _refreshRepo.Object,
            opt);
    }

    [Fact]
    public async Task Handle_Should_Remove_And_InvalidateCache()
    {
        var handler = CreateHandler(revokeSessions: false);
        var claimId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var cmd = new RemovePermissionFromClaimCommand(claimId, permissionId);

        var userIds = new List<Guid> { Guid.NewGuid() };
        _repo.Setup(r => r.GetUserIdsByOperationClaimIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

        await handler.Handle(cmd, CancellationToken.None);

        _repo.Verify(r => r.RemoveAsync(claimId, permissionId, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.GetUserIdsByOperationClaimIdAsync(claimId, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUsers(userIds), Times.Once);
        _refreshRepo.Verify(r => r.RevokeAllByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_RevokeSessions_ForAffectedUsers_When_OptionEnabled()
    {
        var handler = CreateHandler(revokeSessions: true);
        var claimId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var cmd = new RemovePermissionFromClaimCommand(claimId, permissionId);

        var userIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _repo.Setup(r => r.GetUserIdsByOperationClaimIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

        await handler.Handle(cmd, CancellationToken.None);

        _repo.Verify(r => r.RemoveAsync(claimId, permissionId, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUsers(userIds), Times.Once);
        foreach (var userId in userIds)
            _refreshRepo.Verify(r => r.RevokeAllByUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _refreshRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
