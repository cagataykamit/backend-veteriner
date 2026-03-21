using Backend.Veteriner.Application.Auth.Commands.Permissions.Create;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.Permissions.Validators;

public sealed class CreatePermissionCommandValidatorTests
{
    private readonly CreatePermissionCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_Code_IsEmpty()
    {
        // Arrange
        var cmd = new CreatePermissionCommand(string.Empty, "desc");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePermissionCommand.Code));
    }

    [Fact]
    public void Validate_Should_Fail_When_Code_TooLong()
    {
        // Arrange
        var longCode = new string('a', 129);
        var cmd = new CreatePermissionCommand(longCode, "desc");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePermissionCommand.Code));
    }

    [Fact]
    public void Validate_Should_Fail_When_Code_HasInvalidCharacters()
    {
        // Arrange
        var cmd = new CreatePermissionCommand("INVALID CODE!", "desc");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePermissionCommand.Code));
    }

    [Fact]
    public void Validate_Should_Fail_When_Description_TooLong()
    {
        // Arrange
        var longDesc = new string('d', 513);
        var cmd = new CreatePermissionCommand("perm.code", longDesc);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePermissionCommand.Description));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var cmd = new CreatePermissionCommand("perm.code-1:read", "Some description");

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

