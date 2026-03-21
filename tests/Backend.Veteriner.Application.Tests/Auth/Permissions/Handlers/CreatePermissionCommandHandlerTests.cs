using Backend.Veteriner.Application.Auth.Commands.Permissions.Create;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Authorization;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Permissions.Handlers;

public sealed class CreatePermissionCommandHandlerTests
{
    private readonly Mock<IPermissionRepository> _repo = new();

    private CreatePermissionCommandHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_CodeAlreadyExists()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new CreatePermissionCommand("perm.code", "desc");

        _repo.Setup(r => r.ExistsByCodeAsync("perm.code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Permissions.DuplicateCode");
        _repo.Verify(r => r.AddAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreatePermission_When_CodeIsUnique()
    {
        // Arrange
        var handler = CreateHandler();
        var cmd = new CreatePermissionCommand(" perm.code ", "desc");

        _repo.Setup(r => r.ExistsByCodeAsync("perm.code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Permission? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()))
            .Callback<Permission, CancellationToken>((p, _) => captured = p)
            .Returns(Task.CompletedTask);

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        captured.Should().NotBeNull();
        captured!.Code.Should().Be("perm.code");
        captured.Description.Should().Be("desc");
    }
}

