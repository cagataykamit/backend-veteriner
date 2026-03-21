using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Users.Queries.Claims.GetByUserId;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Users.Queries;

public sealed class AdminGetUserClaimsQueryHandlerTests
{
    private readonly Mock<IUserOperationClaimRepository> _repo = new();

    private AdminGetUserClaimsQueryHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnEmptyList_When_RepositoryReturnsEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new AdminGetUserClaimsQuery(userId);

        _repo.Setup(r => r.GetDetailsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserOperationClaimDetailDto>());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_ReturnDetails_FromRepository()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new AdminGetUserClaimsQuery(userId);

        var detail1 = new UserOperationClaimDetailDto(Guid.NewGuid(), userId, "user@example.com", Guid.NewGuid(), "Admin");
        var detail2 = new UserOperationClaimDetailDto(Guid.NewGuid(), userId, "user@example.com", Guid.NewGuid(), "Editor");

        _repo.Setup(r => r.GetDetailsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOperationClaimDetailDto> { detail1, detail2 });

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(new[] { detail1, detail2 });
    }
}

