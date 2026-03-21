using Backend.Veteriner.Application.Users.Commands.Claims.Remove;
using Backend.Veteriner.Application.Users.Commands.Claims.Remove.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Users.Validators;

public sealed class AdminRemoveUserClaimCommandValidatorTests
{
    private readonly AdminRemoveUserClaimCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_UserId_IsEmpty()
    {
        // Arrange
        var command = new AdminRemoveUserClaimCommand(Guid.Empty, Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminRemoveUserClaimCommand.UserId));
    }

    [Fact]
    public void Validate_Should_Fail_When_OperationClaimId_IsEmpty()
    {
        // Arrange
        var command = new AdminRemoveUserClaimCommand(Guid.NewGuid(), Guid.Empty);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminRemoveUserClaimCommand.OperationClaimId));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var command = new AdminRemoveUserClaimCommand(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

