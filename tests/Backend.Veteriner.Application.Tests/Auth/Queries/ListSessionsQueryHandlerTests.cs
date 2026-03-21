using Backend.Veteriner.Application.Auth.Queries.Sessions;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Queries;

public sealed class ListSessionsQueryHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private readonly Mock<IClientContext> _client = new();

    private ListSessionsQueryHandler CreateHandler()
        => new(_repo.Object, _client.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_UserIsUnauthenticated()
    {
        // Arrange
        var handler = CreateHandler();
        var query = new ListSessionsQuery();

        _client.SetupGet(c => c.UserId).Returns((Guid?)null);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Sessions.Unauthorized");
        _repo.Verify(r => r.GetByUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyList_When_UserHasNoSessions()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new ListSessionsQuery();

        _client.SetupGet(c => c.UserId).Returns(userId);
        _repo.Setup(r => r.GetByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken>());

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_ReturnSessions_When_UserHasSessions()
    {
        // Arrange
        var handler = CreateHandler();
        var userId = Guid.NewGuid();
        var query = new ListSessionsQuery();

        _client.SetupGet(c => c.UserId).Returns(userId);

        var active = new RefreshToken(userId, "hash1", DateTime.UtcNow.AddDays(1), "1.1.1.1", "ua1");
        var revoked = new RefreshToken(userId, "hash2", DateTime.UtcNow.AddDays(1), "2.2.2.2", "ua2");
        revoked.Revoke("revoked");

        _repo.Setup(r => r.GetByUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RefreshToken> { active, revoked });

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        var dtos = result.Value.ToList();
        dtos.Select(d => d.Id).Should().Contain(new[] { active.Id, revoked.Id });
    }
}

