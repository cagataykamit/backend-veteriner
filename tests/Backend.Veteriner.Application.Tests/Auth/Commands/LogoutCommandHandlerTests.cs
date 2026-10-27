using Backend.Veteriner.Application.Auth.Commands.Logout;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Users;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

/// <summary>Oturumun aktifliği istek anında TimeProvider saatiyle değerlendirilir (B4).</summary>
public sealed class LogoutCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private readonly FakeTimeProvider _time = new(Now);

    private LogoutCommandHandler CreateHandler() => new(_repo.Object, _time);

    private RefreshToken Token(DateTime expiresAtUtc)
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", expiresAtUtc, null, null, Now.UtcDateTime.AddDays(-1));
        _repo.Setup(r => r.GetByTokenAsync("raw", It.IsAny<CancellationToken>())).ReturnsAsync(token);
        return token;
    }

    [Fact]
    public async Task Handle_Should_Revoke_When_Token_Active_At_Request_Time()
    {
        var token = Token(Now.UtcDateTime.AddMinutes(1));

        await CreateHandler().Handle(new LogoutCommand("raw"), CancellationToken.None);

        _repo.Verify(r => r.RevokeAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Skip_When_Token_Expired_At_Request_Time()
    {
        Token(Now.UtcDateTime.AddMinutes(1));
        _time.Advance(TimeSpan.FromMinutes(1));

        await CreateHandler().Handle(new LogoutCommand("raw"), CancellationToken.None);

        _repo.Verify(r => r.RevokeAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
