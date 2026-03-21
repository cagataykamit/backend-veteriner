using Backend.Veteriner.Application.Auth.Commands.Permissions.Delete;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Permissions.Handlers;

public sealed class DeletePermissionCommandHandlerTests
{
    private readonly Mock<IPermissionRepository> _repo = new();
    private readonly Mock<IOperationClaimPermissionRepository> _ocpRepo = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private DeletePermissionCommandHandler CreateHandler(bool revokeSessions = false)
    {
        var opt = Options.Create(new PermissionChangeOptions
        {
            RevokeSessionsOnPermissionChange = revokeSessions
        });

        return new DeletePermissionCommandHandler(
            _repo.Object,
            _ocpRepo.Object,
            _cache.Object,
            _refreshRepo.Object,
            _uow.Object,
            opt);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new DeletePermissionCommand(Guid.NewGuid());

        _repo.Setup(r => r.GetByIdAsync(cmd.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Permissions.NotFound");
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_DeletePermission_And_InvalidateCache_When_Successful()
    {
        // Arrange
        var handler = CreateHandler(revokeSessions: false);
        var id = Guid.NewGuid();
        var cmd = new DeletePermissionCommand(id);

        var existing = new Permission("CODE", "desc");
        typeof(Permission).GetProperty("Id")!.SetValue(existing, id);

        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var affectedUsers = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _ocpRepo.Setup(r => r.GetUserIdsByPermissionIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(affectedUsers);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _ocpRepo.Verify(r => r.RemoveAllByPermissionIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(c => c.InvalidateUsers(affectedUsers), Times.Once);
        _refreshRepo.Verify(r => r.RevokeAllByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_RevokeSessions_ForAffectedUsers_When_OptionEnabled()
    {
        // Arrange
        var handler = CreateHandler(revokeSessions: true);
        var id = Guid.NewGuid();
        var cmd = new DeletePermissionCommand(id);

        var existing = new Permission("CODE", "desc");
        typeof(Permission).GetProperty("Id")!.SetValue(existing, id);

        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var affectedUsers = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _ocpRepo.Setup(r => r.GetUserIdsByPermissionIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(affectedUsers);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        foreach (var userId in affectedUsers)
        {
            _refreshRepo.Verify(r => r.RevokeAllByUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}

