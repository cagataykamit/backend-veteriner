using Backend.Veteriner.Application.Users.Commands.Create;
using Backend.Veteriner.Application.Users.Commands.Create.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Users.Validators;

public sealed class AdminCreateUserCommandValidatorTests
{
    private readonly AdminCreateUserCommandValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Should_Fail_When_Email_IsEmpty(string email)
    {
        // Arrange
        var command = new AdminCreateUserCommand(email, "Password123!");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminCreateUserCommand.Email));
    }

    [Fact]
    public void Validate_Should_Fail_When_Email_IsInvalidFormat()
    {
        // Arrange
        var command = new AdminCreateUserCommand("invalid-email", "Password123!");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminCreateUserCommand.Email));
    }

    [Fact]
    public void Validate_Should_Fail_When_Email_IsTooLong()
    {
        // Arrange
        var longLocal = new string('a', 201);
        var command = new AdminCreateUserCommand($"{longLocal}@example.com", "Password123!");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminCreateUserCommand.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Should_Fail_When_Password_IsEmpty(string password)
    {
        // Arrange
        var command = new AdminCreateUserCommand("user@example.com", password);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminCreateUserCommand.Password));
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_IsTooShort()
    {
        // Arrange
        var command = new AdminCreateUserCommand("user@example.com", "short");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminCreateUserCommand.Password));
    }

    [Fact]
    public void Validate_Should_Fail_When_Password_IsTooLong()
    {
        // Arrange
        var longPassword = new string('p', 129);
        var command = new AdminCreateUserCommand("user@example.com", longPassword);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(AdminCreateUserCommand.Password));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var command = new AdminCreateUserCommand("user@example.com", "StrongPass123");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

