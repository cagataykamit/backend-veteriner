using Backend.Veteriner.Application.Auth.PasswordReset.Commands.Request;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.PasswordReset;

public sealed class RequestPasswordResetHandlerTests
{
    private const string FrontendBaseUrl = "http://localhost:4200";

    private readonly Mock<IUserReadRepository> _users = new();
    private readonly Mock<IVerificationTokenRepository> _repo = new();
    private readonly Mock<ITokenHashService> _hash = new();
    private readonly Mock<IEmailSender> _email = new();

    private RequestPasswordResetHandler CreateHandler(AppOptions? appOptions = null)
        => new(
            _users.Object,
            _repo.Object,
            _hash.Object,
            _email.Object,
            Options.Create(appOptions ?? new AppOptions { FrontendBaseUrl = FrontendBaseUrl }),
            NullLogger<RequestPasswordResetHandler>.Instance);

    [Fact]
    public async Task Handle_Should_SendSpaResetLink_When_UserExists()
    {
        // Arrange
        var handler = CreateHandler();
        var user = new User("user@example.com", "hash");
        string? capturedRaw = null;
        string? capturedBody = null;

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _repo.Setup(r => r.GetActiveByUserAsync(user.Id, VerificationPurpose.PasswordReset, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerificationToken?)null);
        _hash.Setup(h => h.ComputeSha256(It.IsAny<string>()))
            .Callback<string>(raw => capturedRaw = raw)
            .Returns("hash");
        _email.Setup(e => e.SendAsync(user.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<string, string, string, CancellationToken, bool>((_, _, body, _, _) => capturedBody = body)
            .Returns(Task.CompletedTask);

        // Act
        await handler.Handle(new RequestPasswordResetCommand("user@example.com"), CancellationToken.None);

        // Assert
        capturedRaw.Should().NotBeNullOrEmpty();
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain($"{FrontendBaseUrl}/auth/reset-password?token={Uri.EscapeDataString(capturedRaw!)}");
        capturedBody.Should().NotContain("/api/password/confirm");

        _email.Verify(
            e => e.SendAsync(user.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Once);
        _repo.Verify(r => r.AddAsync(It.IsAny<VerificationToken>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_TrimTrailingSlash_FromFrontendBaseUrl()
    {
        // Arrange
        var handler = CreateHandler(new AppOptions { FrontendBaseUrl = "http://localhost:4200/" });
        var user = new User("user@example.com", "hash");
        string? capturedRaw = null;
        string? capturedBody = null;

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _repo.Setup(r => r.GetActiveByUserAsync(user.Id, VerificationPurpose.PasswordReset, It.IsAny<CancellationToken>()))
            .ReturnsAsync((VerificationToken?)null);
        _hash.Setup(h => h.ComputeSha256(It.IsAny<string>()))
            .Callback<string>(raw => capturedRaw = raw)
            .Returns("hash");
        _email.Setup(e => e.SendAsync(user.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<string, string, string, CancellationToken, bool>((_, _, body, _, _) => capturedBody = body)
            .Returns(Task.CompletedTask);

        // Act
        await handler.Handle(new RequestPasswordResetCommand("user@example.com"), CancellationToken.None);

        // Assert
        capturedBody.Should().Contain($"{FrontendBaseUrl}/auth/reset-password?token={Uri.EscapeDataString(capturedRaw!)}");
        capturedBody.Should().NotContain("4200//auth/reset-password");
    }

    [Fact]
    public async Task Handle_Should_NotSendEmail_When_UserNotFound()
    {
        // Arrange
        var handler = CreateHandler();

        _users.Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        await handler.Handle(new RequestPasswordResetCommand("missing@example.com"), CancellationToken.None);

        // Assert
        _email.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Never);
        _repo.Verify(r => r.AddAsync(It.IsAny<VerificationToken>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
