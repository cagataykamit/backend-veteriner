using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Backend.IntegrationTests.Auth;

public sealed class LoginEndpointTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LoginEndpointTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

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
}

