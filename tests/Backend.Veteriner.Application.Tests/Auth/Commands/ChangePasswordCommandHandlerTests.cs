using Backend.Veteriner.Application.Auth.Commands.ChangePassword;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

public sealed class ChangePasswordCommandHandlerTests
{
    private const string CurrentPassword = "OldPass1!";
    private const string NewPassword = "NewPass2@";
    private const string StoredHash = "stored-hash";
    private const string NewHash = "new-hash";

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IClientContext> _client = new();
    private readonly ChangePasswordCommandValidator _validator = new();

    private ChangePasswordCommandHandler CreateHandler()
        => new(_users.Object, _hasher.Object, _client.Object);

    private static ChangePasswordCommand ValidCommand()
        => new(CurrentPassword, NewPassword, NewPassword);

    [Fact]
    public async Task Handle_Should_UpdatePasswordHash_When_CurrentPasswordIsValid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("user@example.com", StoredHash);
        var handler = CreateHandler();
        var command = ValidCommand();

        _client.Setup(c => c.UserId).Returns(userId);
        _users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(CurrentPassword, StoredHash)).Returns(true);
        _hasher.Setup(h => h.Verify(NewPassword, StoredHash)).Returns(false);
        _hasher.Setup(h => h.Hash(NewPassword)).Returns(NewHash);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Should().Be(NewHash);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_CurrentPasswordIsInvalid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("user@example.com", StoredHash);
        var handler = CreateHandler();

        _client.Setup(c => c.UserId).Returns(userId);
        _users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(CurrentPassword, StoredHash)).Returns(false);

        // Act
        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.ChangePassword.InvalidCurrentPassword");
        result.Error.Message.Should().Be("Mevcut şifre hatalı.");
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
        _users.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_AuthenticatedUserIdIsMissing()
    {
        // Arrange
        var handler = CreateHandler();
        _client.Setup(c => c.UserId).Returns((Guid?)null);

        // Act
        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
        _users.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var handler = CreateHandler();

        _client.Setup(c => c.UserId).Returns(userId);
        _users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.ChangePassword.UserNotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_NewPasswordMatchesCurrentPasswordHash()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = new User("user@example.com", StoredHash);
        var handler = CreateHandler();

        _client.Setup(c => c.UserId).Returns(userId);
        _users.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _hasher.Setup(h => h.Verify(CurrentPassword, StoredHash)).Returns(true);
        _hasher.Setup(h => h.Verify(NewPassword, StoredHash)).Returns(true);

        // Act
        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.ChangePassword.SameAsCurrent");
        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Validate_Should_Fail_When_CurrentPasswordIsEmpty()
    {
        // Arrange
        var command = new ChangePasswordCommand("", NewPassword, NewPassword);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword));
    }

    [Fact]
    public void Validate_Should_Fail_When_ConfirmPasswordDoesNotMatchNewPassword()
    {
        // Arrange
        var command = new ChangePasswordCommand(CurrentPassword, NewPassword, "Mismatch1!");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordCommand.ConfirmPassword));
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPasswordIsWeak()
    {
        // Arrange
        var command = new ChangePasswordCommand(CurrentPassword, "weak", "weak");

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPasswordEqualsCurrentPassword()
    {
        // Arrange
        var command = new ChangePasswordCommand(CurrentPassword, CurrentPassword, CurrentPassword);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == nameof(ChangePasswordCommand.NewPassword)
            && e.ErrorMessage.Contains("aynı", StringComparison.OrdinalIgnoreCase));
    }
}
