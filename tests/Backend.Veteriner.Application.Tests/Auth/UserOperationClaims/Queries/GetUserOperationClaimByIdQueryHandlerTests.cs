using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Auth.Queries.UserOperationClaims.GetById;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Auth;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.UserOperationClaims.Queries;

public sealed class GetUserOperationClaimByIdQueryHandlerTests
{
    private readonly Mock<IUserOperationClaimRepository> _repo = new();

    private GetUserOperationClaimByIdQueryHandler CreateHandler()
        => new(_repo.Object);

    [Fact]
    public async Task Handle_Should_ReturnNull_When_EntityNotFound()
    {
        // Arrange
        var handler = CreateHandler();
        var id = Guid.NewGuid();
        var query = new GetUserOperationClaimByIdQuery(id);

        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserOperationClaim?)null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_MapEntityToDto_When_EntityExists()
    {
        // Arrange
        var handler = CreateHandler();
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var query = new GetUserOperationClaimByIdQuery(id);

        var entity = new UserOperationClaim(userId, claimId);
        typeof(UserOperationClaim).GetProperty(nameof(UserOperationClaim.Id))!
            .SetValue(entity, id);

        _repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(new UserOperationClaimDto(id, userId, claimId));
    }
}

