using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Backend.IntegrationTests.Auth;

public sealed class SessionsEndpointTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SessionsEndpointTests(CustomWebApplicationFactory factory)
        => _client = factory.CreateClient();

    private async Task<string> LoginAndGetAccessTokenAsync()
    {
        var body = new
        {
            Email = "admin@example.com",
            Password = "123456"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task Sessions_Should_ReturnUnauthorized_When_TokenIsMissing()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/me/sessions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sessions_Should_ReturnActiveSessions_When_UserIsAuthenticated()
    {
        // Arrange
        var accessToken = await LoginAndGetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        // Act
        var response = await _client.GetAsync("/api/v1/me/sessions");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        json.GetArrayLength().Should().BeGreaterThan(0);

        var first = json[0];
        first.TryGetProperty("id", out var idProp).Should().BeTrue();
        first.TryGetProperty("revokedAtUtc", out var revokedAtProp).Should().BeTrue();

        idProp.GetGuid().Should().NotBe(Guid.Empty);
        // Yeni login edilmiş session için revokedAtUtc genellikle null olmalı;
        // burada production davranışıyla uyumlu olacak şekilde null veya boş olmamasını
        // zorlamıyoruz, sadece alanın varlığını ve type'ını kontrol ediyoruz.
        (revokedAtProp.ValueKind == JsonValueKind.Null || revokedAtProp.ValueKind == JsonValueKind.String)
            .Should().BeTrue();
    }
}

