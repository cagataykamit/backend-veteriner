using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetByUserId;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.UserOperationClaims.Queries;

public sealed class GetUserOperationClaimsByUserIdQueryHandlerTests
{
    private readonly Mock<IUserOperationClaimRepository> _repo = new();

    private GetUserOperationClaimsByUserIdQueryHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnEmptyList_When_RepositoryReturnsEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new GetUserOperationClaimsByUserIdQuery(userId);

        _repo.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserOperationClaim>());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_MapEntitiesToDtos_When_RepositoryReturnsData()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var claimId1 = Guid.NewGuid();
        var claimId2 = Guid.NewGuid();
        var query = new GetUserOperationClaimsByUserIdQuery(userId);

        var entity1 = new UserOperationClaim(userId, claimId1);
        var entity2 = new UserOperationClaim(userId, claimId2);

        _repo.Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserOperationClaim> { entity1, entity2 });

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(new[]
        {
            new UserOperationClaimDto(entity1.Id, userId, claimId1),
            new UserOperationClaimDto(entity2.Id, userId, claimId2)
        });
    }
}

