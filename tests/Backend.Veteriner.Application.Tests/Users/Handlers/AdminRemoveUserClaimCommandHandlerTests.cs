using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Commands.Claims.Remove;
using Moq;

namespace Backend.Veteriner.Application.Tests.Users.Handlers;

public sealed class AdminRemoveUserClaimCommandHandlerTests
{
    private readonly Mock<IUserOperationClaimRepository> _repo = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private AdminRemoveUserClaimCommandHandler CreateHandler()
        => new(_repo.Object, _cache.Object, _uow.Object);

    [Fact]
    public async Task Handle_Should_RemoveRelation_SaveChanges_And_InvalidateCache()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var command = new AdminRemoveUserClaimCommand(userId, claimId);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.RemoveAsync(userId, claimId, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUser(userId), Times.Once);
    }
}

