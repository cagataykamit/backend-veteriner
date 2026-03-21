using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Remove;
using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.UserOperationClaims.Validators;

public sealed class RemoveUserOperationClaimCommandValidatorTests
{
    private readonly RemoveUserOperationClaimCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_UserId_IsEmpty()
    {
        // Arrange
        var command = new RemoveUserOperationClaimCommand(Guid.Empty, Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RemoveUserOperationClaimCommand.UserId));
    }

    [Fact]
    public void Validate_Should_Fail_When_OperationClaimId_IsEmpty()
    {
        // Arrange
        var command = new RemoveUserOperationClaimCommand(Guid.NewGuid(), Guid.Empty);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RemoveUserOperationClaimCommand.OperationClaimId));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var command = new RemoveUserOperationClaimCommand(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

