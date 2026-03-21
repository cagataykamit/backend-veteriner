using Backend.Veteriner.Application.Auth.Commands.OperationClaimPermissions.Add;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Claims.Handlers;

public sealed class AddPermissionToClaimCommandHandlerTests
{
    private readonly Mock<IOperationClaimPermissionRepository> _repo = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();

    private AddPermissionToClaimCommandHandler CreateHandler(bool revokeSessions = false)
    {
        var opt = Options.Create(new PermissionChangeOptions
        {
            RevokeSessionsOnPermissionChange = revokeSessions
        });
        return new AddPermissionToClaimCommandHandler(
            _repo.Object,
            _cache.Object,
            _refreshRepo.Object,
            opt);
    }

    [Fact]
    public async Task Handle_Should_NotAdd_When_RelationAlreadyExists()
    {
        var handler = CreateHandler();
        var claimId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var cmd = new AddPermissionToClaimCommand(claimId, permissionId);

        _repo.Setup(r => r.ExistsAsync(claimId, permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var userIds = new List<Guid> { Guid.NewGuid() };
        _repo.Setup(r => r.GetUserIdsByOperationClaimIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

        await handler.Handle(cmd, CancellationToken.None);

        _repo.Verify(r => r.AddAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(c => c.InvalidateUsers(userIds), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Add_And_InvalidateCache_When_RelationDoesNotExist()
    {
        var handler = CreateHandler(revokeSessions: false);
        var claimId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var cmd = new AddPermissionToClaimCommand(claimId, permissionId);

        _repo.Setup(r => r.ExistsAsync(claimId, permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var userIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _repo.Setup(r => r.GetUserIdsByOperationClaimIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

        await handler.Handle(cmd, CancellationToken.None);

        _repo.Verify(r => r.AddAsync(claimId, permissionId, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUsers(userIds), Times.Once);
        _refreshRepo.Verify(r => r.RevokeAllByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_RevokeSessions_ForAffectedUsers_When_OptionEnabled()
    {
        var handler = CreateHandler(revokeSessions: true);
        var claimId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var cmd = new AddPermissionToClaimCommand(claimId, permissionId);

        _repo.Setup(r => r.ExistsAsync(claimId, permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var userIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _repo.Setup(r => r.GetUserIdsByOperationClaimIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

        await handler.Handle(cmd, CancellationToken.None);

        _repo.Verify(r => r.AddAsync(claimId, permissionId, It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUsers(userIds), Times.Once);
        foreach (var userId in userIds)
            _refreshRepo.Verify(r => r.RevokeAllByUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _refreshRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
