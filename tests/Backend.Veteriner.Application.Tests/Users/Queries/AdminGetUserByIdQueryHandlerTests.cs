using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Queries.GetById;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Users.Queries;

public sealed class AdminGetUserByIdQueryHandlerTests
{
    private readonly Mock<IUserReadRepository> _users = new();

    private AdminGetUserByIdQueryHandler CreateHandler()
        => new(_users.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var id = Guid.NewGuid();
        var query = new AdminGetUserByIdQuery(id);

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Users.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetailDto_When_UserExists()
    {
        // Arrange
        var handler = CreateHandler();
        var id = Guid.NewGuid();
        var query = new AdminGetUserByIdQuery(id);

        var user = new User("user@example.com", "hash");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, id);
        typeof(User).GetProperty(nameof(User.EmailConfirmed))!.SetValue(user, true);
        typeof(User).GetProperty(nameof(User.CreatedAtUtc))!.SetValue(user, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // fake roles collection via reflection if needed – here we depend only on Names we set
        var rolesField = typeof(User).GetField("_roles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rolesList = (IList<UserRole>)(rolesField!.GetValue(user) ?? new List<UserRole>());
        rolesList.Add(new UserRole("Admin"));
        rolesList.Add(new UserRole("Editor"));

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new AdminUserDetailDto(
            id,
            "user@example.com",
            true,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new[] { "Admin", "Editor" }
        ));
    }
}

