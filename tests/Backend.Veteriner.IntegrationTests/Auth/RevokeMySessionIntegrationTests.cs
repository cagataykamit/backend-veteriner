using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Backend.IntegrationTests.Auth;

public sealed class RevokeMySessionIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RevokeMySessionIntegrationTests(CustomWebApplicationFactory factory)
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

    private static async Task<JsonElement> GetSessionsArrayAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me/sessions");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.Should().Be(JsonValueKind.Array);
        return json;
    }

    private static bool TryGetFirstActiveSessionId(JsonElement sessions, out Guid sessionId)
    {
        foreach (var el in sessions.EnumerateArray())
        {
            if (!el.TryGetProperty("revokedAtUtc", out var revoked))
            {
                continue;
            }

            if (revoked.ValueKind != JsonValueKind.Null)
            {
                continue;
            }

            sessionId = el.GetProperty("id").GetGuid();
            return true;
        }

        sessionId = default;
        return false;
    }

    [Fact]
    public async Task RevokeMySession_Should_ReturnUnauthorized_When_TokenMissing()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;
        var arbitrarySessionId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/me/sessions/{arbitrarySessionId:D}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeMySession_Should_RevokeOwnedSession_When_RequestIsValid()
    {
        // Arrange
        var accessToken = await LoginAndGetAccessTokenAsync();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var sessionsBefore = await GetSessionsArrayAsync(_client);
        TryGetFirstActiveSessionId(sessionsBefore, out var sessionId).Should().BeTrue(
            "login should create at least one active refresh session");

        // Act
        var revokeResponse = await _client.DeleteAsync($"/api/v1/me/sessions/{sessionId:D}");

        // Assert — Result.Success() -> 204 NoContent
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionsAfter = await GetSessionsArrayAsync(_client);
        JsonElement? target = null;
        foreach (var el in sessionsAfter.EnumerateArray())
        {
            if (el.GetProperty("id").GetGuid() == sessionId)
            {
                target = el;
                break;
            }
        }

        target.Should().NotBeNull();
        var revokedProp = target!.Value.GetProperty("revokedAtUtc");
        revokedProp.ValueKind.Should().NotBe(JsonValueKind.Null);
    }
}
