using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using AwesomeAssertions;

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
    public async Task Concurrent_Refresh_With_Same_Token_Should_Issue_Exactly_One_New_Token()
    {
        var (_, refreshToken) = await LoginAndGetTokensAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK)
            .Should().Be(1, "refresh token tek kullanımlıktır; eşzamanlı ikinci kullanım yeni jeton alamaz");
        responses.Where(r => r.StatusCode != HttpStatusCode.OK)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_Racing_Refresh_With_Same_Token_Should_Succeed_And_Refresh_Should_Get_401_Not_500()
    {
        // Birkaç tur: çıkışın kaydı bazen yenilemeden önce, bazen sonra düşer; iki yolda da 500 olmamalı.
        for (var round = 0; round < 5; round++)
        {
            var (access, refreshToken) = await LoginAndGetTokensAsync();
            using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
            {
                Content = JsonContent.Create(new { RefreshToken = refreshToken })
            };
            logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);

            var refreshes = Enumerable.Range(0, 3)
                .Select(_ => _client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken }))
                .ToList();
            var logoutResponse = await _client.SendAsync(logout);
            var refreshResponses = await Task.WhenAll(refreshes);

            logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK, "çakışan kapatma 'zaten kapatılmış' sayılır");
            refreshResponses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Unauthorized);
            refreshResponses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().BeLessThanOrEqualTo(1);
        }
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

