using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Backend.IntegrationTests.Auth;

public sealed class RateLimitingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int LoginBurstCount = 12;

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RateLimitingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_Should_NotReturn429_When_ManyRequestsInIntegrationTestsEnvironment()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "123456"
        };

        for (var i = 0; i < LoginBurstCount; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);
            response.StatusCode.Should().NotBe(
                HttpStatusCode.TooManyRequests,
                $"login attempt {i + 1} should not be throttled in IntegrationTests");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Login_Should_StillReturnUnauthorized_When_PasswordInvalid()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "wrong-password"
        };

        for (var i = 0; i < LoginBurstCount; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);
            response.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized,
                $"invalid login attempt {i + 1} should remain 401, not rate limited");
        }
    }

    [Fact]
    public async Task ProtectedEndpoint_Should_StillReturn403_When_PermissionMissing()
    {
        var token = await IntegrationTestAuthHelper.IssueUserAccessTokenAsync(
            _factory.Services,
            "admin@example.com",
            ["Permissions.Read"]);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/products");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
