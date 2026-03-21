using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Commands.Create;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Users.Handlers;

public sealed class AdminCreateUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _usersWrite = new();
    private readonly Mock<IReadRepository<User>> _usersRead = new();
    private readonly Mock<IPasswordHasher> _hasher = new();

    private AdminCreateUserCommandHandler CreateHandler()
        => new(_usersWrite.Object, _usersRead.Object, _hasher.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_EmailAlreadyExists()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new AdminCreateUserCommand("existing@example.com", "Password123!");

        _usersRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User("existing@example.com", "hash"));

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Users.DuplicateEmail");

        _hasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
        _usersWrite.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _usersWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateUser_And_SaveChanges_When_EmailIsUnique()
    {
        // Arrange
        var handler = CreateHandler();
        var command = new AdminCreateUserCommand("new@example.com", "Password123!");

        _usersRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _hasher.Setup(h => h.Hash(command.Password))
            .Returns("hashed-password");

        User? captured = null;
        _usersWrite.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => captured = u);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        captured.Should().NotBeNull();
        captured!.Email.Should().Be("new@example.com");
        captured.PasswordHash.Should().Be("hashed-password");

        _usersWrite.Verify(r => r.AddAsync(captured, It.IsAny<CancellationToken>()), Times.Once);
        _usersWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

