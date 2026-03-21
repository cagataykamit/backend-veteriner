using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Backend.IntegrationTests.Auth;

public sealed class RefreshEndpointTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RefreshEndpointTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

    private async Task<(string accessToken, string refreshToken)> LoginAndGetTokensAsync()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "123456"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var access = json.GetProperty("accessToken").GetString()!;
        var refresh = json.GetProperty("refreshToken").GetString()!;
        return (access, refresh);
    }

    [Fact]
    public async Task Refresh_Should_IssueNewTokens_When_RefreshTokenIsValid()
    {
        // Arrange
        var (_, refreshToken) = await LoginAndGetTokensAsync();

        var body = new
        {
            RefreshToken = refreshToken
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("accessToken", out var newAccess).Should().BeTrue();
        json.TryGetProperty("refreshToken", out var newRefresh).Should().BeTrue();

        newAccess.GetString().Should().NotBeNullOrEmpty();
        newRefresh.GetString().Should().NotBeNullOrEmpty();
        newRefresh.GetString().Should().NotBe(refreshToken);
    }

    [Fact]
    public async Task Refresh_Should_ReturnUnauthorized_When_RefreshTokenIsInvalid()
    {
        // Arrange
        var body = new
        {
            RefreshToken = "invalid-refresh-token"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

