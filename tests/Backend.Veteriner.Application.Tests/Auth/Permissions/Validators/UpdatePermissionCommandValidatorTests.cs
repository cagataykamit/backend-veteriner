using Backend.Veteriner.Application.Auth.Commands.Permissions.Update;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.Permissions.Validators;

public sealed class UpdatePermissionCommandValidatorTests
{
    private readonly UpdatePermissionCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_Id_IsEmpty()
    {
        // Arrange
        var cmd = new UpdatePermissionCommand(Guid.Empty, "code", "desc");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdatePermissionCommand.Id));
    }

    [Fact]
    public void Validate_Should_Fail_When_Code_IsEmpty()
    {
        // Arrange
        var cmd = new UpdatePermissionCommand(Guid.NewGuid(), string.Empty, "desc");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdatePermissionCommand.Code));
    }

    [Fact]
    public void Validate_Should_Fail_When_Code_TooLong()
    {
        // Arrange
        var longCode = new string('c', 129);
        var cmd = new UpdatePermissionCommand(Guid.NewGuid(), longCode, "desc");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdatePermissionCommand.Code));
    }

    [Fact]
    public void Validate_Should_Fail_When_Description_TooLong()
    {
        // Arrange
        var longDesc = new string('d', 513);
        var cmd = new UpdatePermissionCommand(Guid.NewGuid(), "code", longDesc);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdatePermissionCommand.Description));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var cmd = new UpdatePermissionCommand(Guid.NewGuid(), "code", "desc");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

