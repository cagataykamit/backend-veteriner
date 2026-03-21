using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Assign;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.UserOperationClaims.Handlers;

public sealed class AssignUserOperationClaimCommandHandlerTests
{
    private readonly Mock<IUserReadRepository> _users = new();
    private readonly Mock<IOperationClaimReadRepository> _operationClaims = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private AssignUserOperationClaimCommandHandler CreateHandler()
        => new(_users.Object, _operationClaims.Object, _userOperationClaims.Object, _uow.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new AssignUserOperationClaimCommand(Guid.NewGuid(), Guid.NewGuid());

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("UserOperationClaims.UserNotFound");
        _operationClaims.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userOperationClaims.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userOperationClaims.Verify(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_OperationClaimNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new AssignUserOperationClaimCommand(Guid.NewGuid(), Guid.NewGuid());

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User("user@example.com", "hash"));

        _operationClaims.Setup(r => r.ExistsAsync(command.OperationClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("UserOperationClaims.OperationClaimNotFound");
        _userOperationClaims.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userOperationClaims.Verify(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_LinkAlreadyExists()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new AssignUserOperationClaimCommand(Guid.NewGuid(), Guid.NewGuid());

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User("user@example.com", "hash"));

        _operationClaims.Setup(r => r.ExistsAsync(command.OperationClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userOperationClaims.Setup(r => r.ExistsAsync(command.UserId, command.OperationClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("UserOperationClaims.Duplicate");
        _userOperationClaims.Verify(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateRelation_And_SaveChanges_When_Successful()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var command = new AssignUserOperationClaimCommand(userId, claimId);

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User("user@example.com", "hash"));

        _operationClaims.Setup(r => r.ExistsAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userOperationClaims.Setup(r => r.ExistsAsync(userId, claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        UserOperationClaim? captured = null;
        _userOperationClaims.Setup(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()))
            .Callback<UserOperationClaim, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(userId);
        captured.OperationClaimId.Should().Be(claimId);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

