using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Commands.Claims.Add;
using Backend.Veteriner.Domain.Auth;
using Moq;

namespace Backend.Veteriner.Application.Tests.Users.Handlers;

public sealed class AdminAddUserClaimCommandHandlerTests
{
    private readonly Mock<IUserOperationClaimRepository> _repo = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private AdminAddUserClaimCommandHandler CreateHandler()
        => new(_repo.Object, _cache.Object, _uow.Object);

    [Fact]
    public async Task Handle_Should_ReturnImmediately_When_LinkAlreadyExists()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var command = new AdminAddUserClaimCommand(userId, claimId);

        _repo.Setup(r => r.ExistsAsync(userId, claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _repo.Verify(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(c => c.InvalidateUser(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_AddRelation_SaveChanges_And_InvalidateCache_When_LinkDoesNotExist()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var command = new AdminAddUserClaimCommand(userId, claimId);

        _repo.Setup(r => r.ExistsAsync(userId, claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repo.Setup(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        _repo.Verify(
            r => r.AddAsync(
                It.Is<UserOperationClaim>(e => e.UserId == userId && e.OperationClaimId == claimId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUser(userId), Times.Once);
    }
}

