using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Backend.IntegrationTests.Auth;

public sealed class RevokeAllMySessionsIntegrationTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RevokeAllMySessionsIntegrationTests(CustomWebApplicationFactory factory)
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

    private static DateTimeOffset ParseExpiresAtUtc(JsonElement session)
    {
        var prop = session.GetProperty("expiresAtUtc");
        if (prop.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.Parse(
                prop.GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
        }

        return prop.GetDateTimeOffset();
    }

    [Fact]
    public async Task RevokeAllMySessions_Should_ReturnUnauthorized_When_TokenMissing()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await _client.DeleteAsync("/api/v1/me/sessions/all");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeAllMySessions_Should_RevokeAllOwnedSessions_When_RequestIsValid()
    {
        // Arrange — aynı kullanıcıyla birden fazla login => birden fazla refresh session
        _client.DefaultRequestHeaders.Authorization = null;
        string? accessToken = null;
        for (var i = 0; i < 3; i++)
        {
            accessToken = await LoginAndGetAccessTokenAsync();
        }

        accessToken.Should().NotBeNull();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var sessionsBefore = await GetSessionsArrayAsync(_client);
        sessionsBefore.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);

        // Act — production: Result.Success -> 204 NoContent
        var revokeAllResponse = await _client.DeleteAsync("/api/v1/me/sessions/all");

        // Assert
        revokeAllResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sessionsAfter = await GetSessionsArrayAsync(_client);
        var now = DateTimeOffset.UtcNow;
        foreach (var el in sessionsAfter.EnumerateArray())
        {
            var expiresAt = ParseExpiresAtUtc(el);
            if (expiresAt > now.AddMinutes(-1))
            {
                el.GetProperty("revokedAtUtc").ValueKind.Should().NotBe(
                    JsonValueKind.Null,
                    because: "RevokeAllByUserAsync revokes non-expired tokens; listing still returns them with RevokedAtUtc set");
            }
        }
    }
}
