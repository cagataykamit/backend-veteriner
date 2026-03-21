using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Assign;
using Backend.Veteriner.Application.Auth.Commands.UserOperationClaims.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.UserOperationClaims.Validators;

public sealed class AssignUserOperationClaimCommandValidatorTests
{
    private readonly AssignUserOperationClaimCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_UserId_IsEmpty()
    {
        // Arrange
        var command = new AssignUserOperationClaimCommand(Guid.Empty, Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AssignUserOperationClaimCommand.UserId));
    }

    [Fact]
    public void Validate_Should_Fail_When_OperationClaimId_IsEmpty()
    {
        // Arrange
        var command = new AssignUserOperationClaimCommand(Guid.NewGuid(), Guid.Empty);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AssignUserOperationClaimCommand.OperationClaimId));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var command = new AssignUserOperationClaimCommand(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

