using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Queries.GetAll;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Users.Queries;

public sealed class AdminGetUsersQueryHandlerTests
{
    private readonly Mock<IUserReadRepository> _users = new();

    private AdminGetUsersQueryHandler CreateHandler()
        => new(_users.Object);

    [Fact]
    public async Task Handle_Should_ReturnEmptyPagedResult_When_RepositoryReturnsEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var pageRequest = new PageRequest { Page = 1, PageSize = 10 };
        var query = new AdminGetUsersQuery(pageRequest);

        var empty = PagedResult<AdminUserListItemDto>.Create(
            items: Array.Empty<AdminUserListItemDto>(),
            totalItems: 0,
            page: pageRequest.Page,
            pageSize: pageRequest.PageSize);

        _users.Setup(r => r.GetAdminPagedAsync(pageRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(empty);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().BeEmpty();
        result.TotalItems.Should().Be(0);
        result.Page.Should().Be(pageRequest.Page);
        result.PageSize.Should().Be(pageRequest.PageSize);
    }

    [Fact]
    public async Task Handle_Should_ForwardPagedResult_FromRepository()
    {
        // Arrange
        var handler = CreateHandler();
        var pageRequest = new PageRequest { Page = 2, PageSize = 5 };
        var query = new AdminGetUsersQuery(pageRequest);

        var items = new[]
        {
            new AdminUserListItemDto(Guid.NewGuid(), "u1@example.com", true, DateTime.UtcNow, new List<string> { "Admin" }),
            new AdminUserListItemDto(Guid.NewGuid(), "u2@example.com", false, DateTime.UtcNow, new List<string> { "User" })
        };

        var paged = PagedResult<AdminUserListItemDto>.Create(
            items: items,
            totalItems: 20,
            page: pageRequest.Page,
            pageSize: pageRequest.PageSize);

        _users.Setup(r => r.GetAdminPagedAsync(pageRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paged);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(paged);
    }
}

