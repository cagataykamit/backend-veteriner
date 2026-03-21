using Backend.Veteriner.Application.Users.Commands.Claims.Add;
using Backend.Veteriner.Application.Users.Commands.Claims.Add.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Users.Validators;

public sealed class AdminAddUserClaimCommandValidatorTests
{
    private readonly AdminAddUserClaimCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_UserId_IsEmpty()
    {
        // Arrange
        var command = new AdminAddUserClaimCommand(Guid.Empty, Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminAddUserClaimCommand.UserId));
    }

    [Fact]
    public void Validate_Should_Fail_When_OperationClaimId_IsEmpty()
    {
        // Arrange
        var command = new AdminAddUserClaimCommand(Guid.NewGuid(), Guid.Empty);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminAddUserClaimCommand.OperationClaimId));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var command = new AdminAddUserClaimCommand(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

