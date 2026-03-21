using Backend.Veteriner.Application.Auth.Commands.Sessions.Revoke;
using Backend.Veteriner.Application.Auth.Commands.Sessions.Revoke.Validators;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Auth.Validators;

public sealed class RevokeSessionCommandValidatorTests
{
    private readonly RevokeSessionCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Fail_When_UserId_IsEmpty()
    {
        // Arrange
        var cmd = new RevokeSessionCommand(
            UserId: Guid.Empty,
            RefreshTokenId: Guid.NewGuid());

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RevokeSessionCommand.UserId));
    }

    [Fact]
    public void Validate_Should_Fail_When_RefreshTokenId_IsEmpty()
    {
        // Arrange
        var cmd = new RevokeSessionCommand(
            UserId: Guid.NewGuid(),
            RefreshTokenId: Guid.Empty);

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RevokeSessionCommand.RefreshTokenId));
    }

    [Fact]
    public void Validate_Should_Succeed_When_Request_IsValid()
    {
        // Arrange
        var cmd = new RevokeSessionCommand(
            UserId: Guid.NewGuid(),
            RefreshTokenId: Guid.NewGuid());

        // Act
        var result = _validator.Validate(cmd);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}

