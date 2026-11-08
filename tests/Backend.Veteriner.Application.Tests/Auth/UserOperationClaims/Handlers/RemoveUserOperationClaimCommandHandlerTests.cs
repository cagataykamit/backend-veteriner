using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;
using Backend.Veteriner.Application.Common.Abstractions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.UserOperationClaims.Handlers;

public sealed class RemoveUserOperationClaimCommandHandlerTests
{
    private readonly Mock<IUserOperationClaimRepository> _repo = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private RemoveUserOperationClaimCommandHandler CreateHandler()
        => new(_repo.Object, _cache.Object, _uow.Object);

    [Fact]
    public async Task Handle_Should_RemoveRelation_Save_And_InvalidateUser()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var command = new RemoveUserOperationClaimCommand(userId, claimId);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.RemoveAsync(userId, claimId, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUser(userId), Times.Once);
    }
}

