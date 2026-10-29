using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Auth;

public sealed class LoginEndpointTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private const string InvalidCredentials = "Auth.Unauthorized.InvalidCredentials";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LoginEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_Should_ReturnTokens_When_CredentialsAreValid()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "123456"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("accessToken", out var access).Should().BeTrue();
        json.TryGetProperty("refreshToken", out var refresh).Should().BeTrue();

        access.GetString().Should().NotBeNullOrEmpty();
        refresh.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_Should_ReturnUnauthorized_When_CredentialsAreInvalid()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "wrong-password"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Should_LockAccount_With_Same_Response_When_MaxFailedAttemptsReached()
    {
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var (email, password, _) = await IntegrationTestAuthHelper.SeedTenantAdminUserAsync(_factory.Services, hasher);

        for (var i = 0; i < SignInLockout.MaxFailedAttempts; i++)
        {
            var wrong = await _client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = "wrong-password" });
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await IntegrationTestProblemDetails.ReadCodeAsync(wrong)).Should().Be(InvalidCredentials);
        }

        var locked = await _client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = password });
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await IntegrationTestProblemDetails.ReadCodeAsync(locked)).Should().Be(InvalidCredentials, "kilit de aynı yanıtı verir");

        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        user.LockedUntilUtc.Should().NotBeNull();
        (await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "Auth.Login.LockedOut" && a.TargetId == user.Id.ToString("D")))
            .Should().Be(1);
    }
}
