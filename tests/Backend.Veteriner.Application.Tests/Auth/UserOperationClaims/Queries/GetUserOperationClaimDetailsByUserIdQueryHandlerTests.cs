using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetDetails;
using Backend.Veteriner.Application.Common.Abstractions;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.UserOperationClaims.Queries;

public sealed class GetUserOperationClaimDetailsByUserIdQueryHandlerTests
{
    private readonly Mock<IUserOperationClaimRepository> _repo = new();

    private GetUserOperationClaimDetailsByUserIdQueryHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnEmptyList_When_RepositoryReturnsEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new GetUserOperationClaimDetailsByUserIdQuery(userId);

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
        var query = new GetUserOperationClaimDetailsByUserIdQuery(userId);

        var detail1 = new UserOperationClaimDetailDto(Guid.NewGuid(), userId, "user1@example.com", Guid.NewGuid(), "Admin");
        var detail2 = new UserOperationClaimDetailDto(Guid.NewGuid(), userId, "user1@example.com", Guid.NewGuid(), "Editor");

        _repo.Setup(r => r.GetDetailsByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOperationClaimDetailDto> { detail1, detail2 });

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(new[] { detail1, detail2 });
    }
}

